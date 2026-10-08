using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public string? CurrentAccessToken { get; private set; }
    public string? CurrentRefreshToken { get; private set; }
    public DateTime? TokenExpiresAt { get; private set; }

    public ApiClient(string baseUrl, TimeSpan? timeout = null)
        : this(new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        }, ownsHttpClient: true)
    {
    }

    public ApiClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private ApiClient(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public void SetBearerToken(string? token)
    {
        CurrentAccessToken = token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            CurrentRefreshToken = null;
            TokenExpiresAt = null;
        }
    }

    private static string? FirstLine(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private void SetSession(LoginResponse response)
    {
        SetBearerToken(response.AccessToken);
        CurrentRefreshToken = response.RefreshToken;
        TokenExpiresAt = response.ExpiresAt;
    }

    public void RestoreSession(LoginResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        SetSession(response);
    }

    public async Task<bool> RefreshTokenAsync(bool force = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CurrentRefreshToken)) return false;

        var refreshTokenBeforeLock = CurrentRefreshToken;
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Another request may have refreshed the rotating token while this one waited.
            if (force && !string.Equals(CurrentRefreshToken, refreshTokenBeforeLock, StringComparison.Ordinal))
            {
                return true;
            }

            if (!force && TokenExpiresAt.HasValue && TokenExpiresAt.Value > DateTime.UtcNow.AddSeconds(30))
            {
                return true;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                "api/v1/auth/refresh-token",
                new { RefreshToken = CurrentRefreshToken },
                _jsonOptions,
                ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LoginResponse>>(_jsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
            {
                return false;
            }

            SetSession(envelope.Data);
            return true;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendWithRefreshAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        var accessTokenUsed = CurrentAccessToken;
        var response = await send();
        if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrWhiteSpace(CurrentRefreshToken))
        {
            await RememberApiFailureAsync(response, ct);
            return response;
        }

        response.Dispose();
        if (!string.Equals(CurrentAccessToken, accessTokenUsed, StringComparison.Ordinal))
        {
            response = await send();
            await RememberApiFailureAsync(response, ct);
            return response;
        }

        if (!await RefreshTokenAsync(force: true, ct: ct))
        {
            response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            await RememberApiFailureAsync(response, ct);
            return response;
        }

        response = await send();
        await RememberApiFailureAsync(response, ct);
        return response;
    }

    private sealed class ApiFailureScope
    {
        public string? Message { get; set; }
    }

    private readonly AsyncLocal<ApiFailureScope?> _lastApiError = new();

    public void ClearLastApiError() => _lastApiError.Value = new ApiFailureScope();

    public string? TakeLastApiError()
    {
        var error = _lastApiError.Value?.Message;
        if (_lastApiError.Value is { } scope) scope.Message = null;
        return error;
    }

    private async Task RememberApiFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var method = response.RequestMessage?.Method.Method ?? "API";
        var uri = response.RequestMessage?.RequestUri;
        var path = uri is null ? "phiên đăng nhập" :
            uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString();
        var detail = await TryExtractErrorMessageAsync(response, ct);
        if (string.IsNullOrWhiteSpace(detail))
            detail = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Phiên đăng nhập đã hết hạn. Đăng nhập lại rồi thử thao tác."
                : response.ReasonPhrase ?? "Máy chủ không trả chi tiết lỗi.";
        (_lastApiError.Value ??= new ApiFailureScope()).Message =
            $"{method} {path} → HTTP {(int)response.StatusCode}: {detail}";
    }

    public async Task<HealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("health", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new HealthResult(response.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return new HealthResult(false, ex.Message);
        }
    }

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/v1/auth/login",
                new LoginRequest(username, password),
                _jsonOptions,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<LoginResponse>>(body, _jsonOptions);

            if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
            {
                return LoginResult.Failed(response.StatusCode == HttpStatusCode.Unauthorized
                    ? "INVALID_CREDENTIALS"
                    : "LOGIN_FAILED");
            }

            SetSession(envelope.Data);
            return LoginResult.Success(envelope.Data);
        }
        catch (TaskCanceledException)
        {
            return LoginResult.Failed("LOGIN_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return LoginResult.Failed("LOGIN_UNAVAILABLE");
        }
        catch (JsonException)
        {
            return LoginResult.Failed("LOGIN_UNAVAILABLE");
        }
        catch (Exception)
        {
            return LoginResult.Failed("LOGIN_FAILED");
        }
    }

    public async Task<(bool Succeeded, string Message)> RequestPasswordResetAsync(
        string usernameOrEmail,
        string? fullName,
        string? phoneNumber,
        string? note,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                UsernameOrEmail = usernameOrEmail,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                Note = note
            };

            using var response = await _httpClient.PostAsJsonAsync(
                "api/v1/auth/forgot-password",
                payload,
                _jsonOptions,
                ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<string>>(body, _jsonOptions);

            if (!response.IsSuccessStatusCode || envelope?.Success != true)
            {
                return (false, envelope?.Message ?? "Yêu cầu cấp lại mật khẩu chưa hoàn tất. Vui lòng thử lại sau.");
            }

            return (true, envelope.Message ?? "Yêu cầu cấp lại mật khẩu đã được gửi thành công đến Quản trị viên Tổng công ty.");
        }
        catch (Exception ex)
        {
            return (false, "Không thể gửi yêu cầu cấp lại mật khẩu: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }

}
