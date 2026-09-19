using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Infrastructure.Integration.Connectors;

/// <summary>
/// Nền tảng dùng chung cho các connector REST read-only đang chạy trên Railway.
/// Có timeout, retry có backoff cho timeout/429/5xx và phân trang page/limit.
/// </summary>
public abstract class RailwayHttpConnectorBase : IExternalConnector
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string? _bearerToken;
    private readonly string? _integrationKey;
    private readonly int _pageSize;

    protected RailwayHttpConnectorBase(
        HttpClient httpClient,
        IConfiguration configuration,
        string configurationKey,
        string defaultBaseUrl,
        ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var section = configuration.GetSection($"Integrations:{configurationKey}");
        var baseUrl = section["BaseUrl"] ?? defaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl))
            throw new InvalidOperationException($"Invalid Railway connector BaseUrl for {configurationKey}.");

        _httpClient.BaseAddress = parsedBaseUrl;
        _httpClient.Timeout = TimeSpan.FromSeconds(ReadPositiveInt(section["TimeoutSeconds"], 30));
        _pageSize = Math.Clamp(ReadPositiveInt(section["PageSize"], 50), 1, 100);
        // Empty JSON placeholders must not mask the real Railway environment variable.
        _bearerToken = FirstNonEmpty(
            section["BearerToken"],
            configuration[$"{configurationKey.ToUpperInvariant()}_BEARER_TOKEN"]);
        _integrationKey = FirstNonEmpty(
            section["IntegrationKey"],
            configuration[$"{configurationKey.ToUpperInvariant()}_INTEGRATION_KEY"]);
    }

    public abstract string SourceSystem { get; }

    public abstract IReadOnlyList<string> SupportedEntityTypes { get; }

    public async Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct)
    {
        if (!SupportedEntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Connector {SourceSystem} không hỗ trợ entity type '{entityType}'. " +
                $"Hỗ trợ: {string.Join(", ", SupportedEntityTypes)}");
        }

        var path = BuildRequestPath(entityType, cursor);
        using var document = await GetJsonAsync(path, ct);
        var rawItems = ExtractItems(document.RootElement);
        var items = NormalizeItems(entityType, rawItems);
        var hasMore = ReadHasMore(document.RootElement, rawItems.Count);
        var nextCursor = new SyncCursor
        {
            Offset = cursor.Offset + rawItems.Count,
            ContinuationToken = ReadString(document.RootElement, "nextCursor", "next_cursor", "continuationToken"),
            UpdatedSince = cursor.UpdatedSince
        };

        return new SyncPage
        {
            Items = items,
            NextCursor = nextCursor,
            HasMore = hasMore,
            TotalAvailable = ReadTotal(document.RootElement)
        };
    }

    public async Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_bearerToken) && string.IsNullOrWhiteSpace(_integrationKey))
        {
            return new ConnectorHealth
            {
                SourceSystem = SourceSystem,
                IsHealthy = false,
                Message = "Connector chưa được cấu hình BearerToken hoặc IntegrationKey."
            };
        }

        try
        {
            using var response = await SendWithRetryAsync(new HttpRequestMessage(HttpMethod.Get, "/health"), ct);
            return new ConnectorHealth
            {
                SourceSystem = SourceSystem,
                IsHealthy = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode ? "Railway health endpoint is healthy." : $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Railway connector health check failed for {SourceSystem}", SourceSystem);
            return new ConnectorHealth
            {
                SourceSystem = SourceSystem,
                IsHealthy = false,
                Message = ex.Message
            };
        }
    }

    protected abstract string BuildRequestPath(string entityType, SyncCursor cursor);

    protected virtual IReadOnlyList<JsonElement> NormalizeItems(
        string entityType,
        IReadOnlyList<JsonElement> rawItems) => rawItems;

    protected int PageSize => _pageSize;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    protected string GetConfiguredPath(
        IConfiguration configuration,
        string configurationKey,
        string optionName,
        string defaultPath)
    {
        return configuration.GetSection($"Integrations:{configurationKey}")[optionName] ?? defaultPath;
    }

    protected string AddPageQuery(string path, SyncCursor cursor)
    {
        var page = cursor.Offset / _pageSize + 1;
        var separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}page={page.ToString(CultureInfo.InvariantCulture)}&limit={_pageSize.ToString(CultureInfo.InvariantCulture)}";
    }

    protected static string? GetString(JsonElement element, params string[] names)
        => ReadString(element, names);

    protected static int GetInt(JsonElement element, params string[] names)
        => ReadInt(element, names) ?? 0;

    protected static decimal GetDecimal(JsonElement element, params string[] names)
        => ReadDecimal(element, names) ?? 0m;

    protected static bool GetBool(JsonElement element, params string[] names)
        => ReadBool(element, names) ?? false;

    protected static DateTime? GetDateTime(JsonElement element, params string[] names)
        => ReadDateTime(element, names);

    protected static JsonElement ToJsonElement(object value)
        => JsonSerializer.SerializeToElement(value, JsonOptions);

    protected static bool TryGetArray(JsonElement element, out IReadOnlyList<JsonElement> items, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(element, name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                items = property.EnumerateArray().Select(static item => item.Clone()).ToList();
                return true;
            }
        }

        items = [];
        return false;
    }

    protected static DateTime? GetNestedDateTime(JsonElement element, string property, params string[] names)
    {
        if (!TryGetPropertyIgnoreCase(element, property, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;

        return ReadDateTime(nested, names);
    }

    protected static int GetNestedInt(JsonElement element, string property, params string[] names)
    {
        if (!TryGetPropertyIgnoreCase(element, property, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return 0;

        return ReadInt(nested, names) ?? 0;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(new HttpRequestMessage(HttpMethod.Get, path), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{SourceSystem} returned invalid JSON for '{path}'.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var requestUri = request.RequestUri?.ToString() ?? string.Empty;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var attemptRequest = new HttpRequestMessage(request.Method, requestUri);
            if (_bearerToken is not null && !string.IsNullOrWhiteSpace(_bearerToken))
            {
                var token = _bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? _bearerToken[7..]
                    : _bearerToken;
                attemptRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            if (_integrationKey is not null && !string.IsNullOrWhiteSpace(_integrationKey))
                attemptRequest.Headers.TryAddWithoutValidation("X-Integration-Key", _integrationKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    "Retrying {SourceSystem} request {Path} after transport error, attempt {Attempt}/{MaxAttempts}",
                    SourceSystem, requestUri, attempt, MaxAttempts);
                await Task.Delay(delay, ct);
                continue;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    "Retrying {SourceSystem} request {Path} after timeout, attempt {Attempt}/{MaxAttempts}",
                    SourceSystem, requestUri, attempt, MaxAttempts);
                await Task.Delay(delay, ct);
                continue;
            }

            if (response.IsSuccessStatusCode)
                return response;

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests
                            || response.StatusCode == HttpStatusCode.RequestTimeout
                            || (int)response.StatusCode >= 500;
            if (!retryable || attempt == MaxAttempts)
            {
                var statusCode = response.StatusCode;
                var message = statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "Authentication/permission denied by external API."
                    : $"External API returned HTTP {(int)statusCode} ({response.ReasonPhrase}).";
                response.Dispose();
                throw new HttpRequestException($"{SourceSystem}: {message}", null, statusCode);
            }

            var responseDelay = response.Headers.RetryAfter?.Delta
                                ?? TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
            response.Dispose();
            _logger.LogWarning(
                "Retrying {SourceSystem} request {Path} after HTTP status, attempt {Attempt}/{MaxAttempts}, delay {DelayMs}ms",
                SourceSystem, requestUri, attempt, MaxAttempts, responseDelay.TotalMilliseconds);
            await Task.Delay(responseDelay, ct);
        }

        throw new InvalidOperationException($"{SourceSystem}: request retry loop ended unexpectedly.");
    }

    private bool ReadHasMore(JsonElement root, int itemCount)
    {
        if (TryGetPropertyIgnoreCase(root, "pagination", out var pagination)
            && pagination.ValueKind == JsonValueKind.Object)
        {
            if (ReadBool(pagination, "hasNext", "has_next", "hasNextPage") is { } hasNext)
                return hasNext;

            var currentPage = ReadInt(pagination, "currentPage", "current_page", "page");
            var totalPages = ReadInt(pagination, "totalPages", "total_pages");
            if (currentPage.HasValue && totalPages.HasValue)
                return currentPage.Value < totalPages.Value;
        }

        return itemCount >= _pageSize;
    }

    private static int? ReadTotal(JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "pagination", out var pagination)
            && pagination.ValueKind == JsonValueKind.Object)
        {
            return ReadInt(pagination, "total", "totalUsers", "total_users", "count");
        }

        return ReadInt(root, "total", "totalUsers", "total_users");
    }

    private static IReadOnlyList<JsonElement> ExtractItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Select(static item => item.Clone()).ToList();

        foreach (var propertyName in new[] { "data", "items", "users", "courses", "exams", "results", "questions" })
        {
            if (!TryGetPropertyIgnoreCase(root, propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.Array)
                return property.EnumerateArray().Select(static item => item.Clone()).ToList();

            if (property.ValueKind == JsonValueKind.Object)
            {
                var nested = ExtractItems(property);
                if (nested.Count > 0)
                    return nested;
            }
        }

        return root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "id", out _)
            ? [root.Clone()]
            : [];
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        var value = ReadString(element, names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static decimal? ReadDecimal(JsonElement element, params string[] names)
    {
        var value = ReadString(element, names);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static bool? ReadBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String
                && bool.TryParse(value.GetString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static DateTime? ReadDateTime(JsonElement element, params string[] names)
    {
        var value = ReadString(element, names);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
            ? result
            : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static int ReadPositiveInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
            ? result
            : fallback;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
