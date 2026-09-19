using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public string? CurrentAccessToken { get; private set; }
    public string? CurrentRefreshToken { get; private set; }
    public DateTime? TokenExpiresAt { get; private set; }

    public ApiClient(string baseUrl, TimeSpan? timeout = null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
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

        await _refreshLock.WaitAsync(ct);
        try
        {
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
        var response = await send();
        if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrWhiteSpace(CurrentRefreshToken))
        {
            return response;
        }

        response.Dispose();
        if (!await RefreshTokenAsync(force: true, ct: ct))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return await send();
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

    // ── EdTech API Calls (Ngày 8 & 9) ──

    public async Task<PaginatedData<CourseItem>?> GetCoursesAsync(string? search = null, string? status = null, CancellationToken ct = default)
    {
        var url = $"api/v1/edtech/courses?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CourseItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCourseAsync(string title, decimal price, string description, string? status = "Published", string? slug = null, CancellationToken ct = default)
    {
        var req = new { Title = title, Price = price, Description = description, Status = status ?? "Published", Slug = slug };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/edtech/courses", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCourseAsync(Guid id, string title, decimal price, string description, string status, string? slug = null, CancellationToken ct = default)
    {
        var req = new { Title = title, Price = price, Description = description, Status = status, Slug = slug };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/edtech/courses/{id}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteCourseAsync(Guid id, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/edtech/courses/{id}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<QuestionItem>?> GetQuestionsAsync(string? search = null, string? difficulty = null, string? status = null, CancellationToken ct = default)
    {
        var url = $"api/v1/edtech/questions?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(difficulty)) url += $"&difficulty={Uri.EscapeDataString(difficulty)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<QuestionItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> PublishQuestionVersionAsync(Guid versionId, string notes, CancellationToken ct = default)
    {
        var req = new { Notes = notes };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/edtech/questions/versions/{versionId}/publish", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<CustomerItem>?> GetCustomersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = $"api/v1/edtech/customers?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CustomerItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    // ── System Sync API Calls (Ngày 6 & 8) ──

    public async Task<SyncTriggerResult?> TriggerSyncAsync(string sourceSystem, string entityType, CancellationToken ct = default)
    {
        var req = new { SourceSystem = sourceSystem, EntityType = entityType, ForceFullSync = false };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/system/sync/trigger", req, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;

        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<SyncTriggerResult>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<SyncRunItem>?> GetSyncRunsAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/system/sync/runs?pageSize=50", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<SyncRunItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<DeadLetterItem>?> GetDeadLettersAsync(bool? resolved = false, CancellationToken ct = default)
    {
        var resolvedQuery = resolved.HasValue ? $"&resolved={resolved.Value.ToString().ToLowerInvariant()}" : string.Empty;
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/system/sync/dead-letters?pageSize=50{resolvedQuery}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<DeadLetterItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RetryDeadLetterAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsync($"api/v1/system/sync/dead-letters/{id}/retry", null, ct), ct);
        return response.IsSuccessStatusCode;
    }

    // ── CSCA Course LMS operations ──

    public async Task<LmsIntegrationOverviewItem?> GetLmsIntegrationOverviewAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/overview", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsIntegrationOverviewItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<LmsCourseMappingItem>?> GetLmsCourseMappingsAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/course-mappings?pageSize=100", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<LmsCourseMappingItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<LmsCourseMappingItem?> UpsertLmsCourseMappingAsync(
        Guid courseId,
        LmsCourseMappingRequest request,
        CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.PutAsJsonAsync($"api/v1/lms-integration/course-mappings/{courseId}", request, _jsonOptions, ct),
            ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsCourseMappingItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<LmsOutboxItem>?> GetLmsOutboxAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/outbox?pageSize=100", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<LmsOutboxItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RetryLmsOutboxAsync(Guid outboxId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsync($"api/v1/lms-integration/outbox/{outboxId}/retry", null, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<LmsOutboxDispatchResultItem?> DispatchLmsOutboxAsync(int batchSize = 20, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsync($"api/v1/lms-integration/outbox/dispatch?batchSize={Math.Clamp(batchSize, 1, 100)}", null, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsOutboxDispatchResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    // ── CSCA & Interview API Calls (Ngày 10) ──

    public async Task<PaginatedData<CscaClassItem>?> GetCscaClassesAsync(string? search = null, string? batch = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/classes?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(batch)) url += $"&batch={Uri.EscapeDataString(batch)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CscaClassItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaStudentDirectoryItem>?> GetCscaStudentDirectoryAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/students";
        if (!string.IsNullOrWhiteSpace(search)) url += $"?search={Uri.EscapeDataString(search)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaStudentDirectoryItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlineMaterialItem>?> GetCscaOnlineMaterialsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/materials?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlineMaterialItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlineVocabularyItem>?> GetCscaOnlineVocabularyAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/vocabulary?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlineVocabularyItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlinePostItem>?> GetCscaOnlinePostsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/posts?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlinePostItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<CscaClassDetailItem?> GetCscaClassDetailAsync(Guid classId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CscaClassDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaClassAsync(string code, string name, string batch, string schedule, decimal tuitionFee, Guid? courseId = null, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Batch = batch, Schedule = schedule, TuitionFee = tuitionFee, CourseId = courseId, Status = "Active" };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/csca/classes", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> EnrollStudentAsync(Guid classId, string studentName, string email, string phone, decimal paidAmount, CancellationToken ct = default)
    {
        var req = new { StudentName = studentName, Email = email, PhoneNumber = phone, PaidAmount = paidAmount, PaymentStatus = paidAmount > 0 ? 1 : 0 };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/students", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> EnrollCscaStudentAsync(
        Guid classId, string studentName, string email, string phone, int? age,
        string hometown, decimal paidAmount, int paymentStatus, string notes, DateTime? debtDueDate = null, CancellationToken ct = default)
    {
        var req = new
        {
            StudentName = studentName,
            Email = email,
            PhoneNumber = phone,
            Age = age,
            Hometown = hometown,
            PaidAmount = paidAmount,
            PaymentStatus = paymentStatus,
            Notes = notes,
            DebtDueDate = debtDueDate
        };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/students", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaClassAsync(
        Guid classId, string name, string batch, string schedule, decimal tuitionFee,
        DateTime? startDate, DateTime? endDate, string status, Guid? courseId = null, CancellationToken ct = default)
    {
        var req = new { Name = name, Batch = batch, Schedule = schedule, TuitionFee = tuitionFee, StartDate = startDate, EndDate = endDate, CourseId = courseId, Status = status };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteCscaClassAsync(Guid classId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaStudentAsync(
        Guid classId, Guid studentId, string studentName, string email, string phone,
        int? age, string hometown, decimal paidAmount, int paymentStatus, string notes, DateTime? debtDueDate = null, CancellationToken ct = default)
    {
        var req = new
        {
            StudentName = studentName,
            Email = email,
            PhoneNumber = phone,
            Age = age,
            Hometown = hometown,
            PaidAmount = paidAmount,
            PaymentStatus = paymentStatus,
            Notes = notes,
            DebtDueDate = debtDueDate
        };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/students/{studentId}/payment", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaStudentAsync(Guid classId, Guid studentId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/students/{studentId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaStaffAsync(Guid classId, Guid staffId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/staff/{staffId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AssignStaffAsync(Guid classId, Guid employeeId, string roleInClass, decimal compensationRate, string? notes = null, CancellationToken ct = default)
    {
        var req = new { EmployeeId = employeeId, RoleInClass = roleInClass, CompensationRate = compensationRate, Notes = notes };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/staff", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaStaffAsync(Guid classId, Guid staffId, string roleInClass, decimal compensationRate, string? notes = null, CancellationToken ct = default)
    {
        var req = new { RoleInClass = roleInClass, CompensationRate = compensationRate, Notes = notes };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/staff/{staffId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AddCscaScheduleAsync(Guid classId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, string? room, string? meetingUrl, string? notes, Guid? classroomId = null, CancellationToken ct = default)
    {
        var req = new { DayOfWeek = dayOfWeek, StartTime = startTime, EndTime = endTime, Room = room, MeetingUrl = meetingUrl, Notes = notes, ClassroomId = classroomId };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/schedules", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaScheduleAsync(Guid classId, Guid scheduleId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, string? room, string? meetingUrl, string? notes, Guid? classroomId = null, CancellationToken ct = default)
    {
        var req = new { DayOfWeek = dayOfWeek, StartTime = startTime, EndTime = endTime, Room = room, MeetingUrl = meetingUrl, Notes = notes, ClassroomId = classroomId };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/schedules/{scheduleId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaScheduleAsync(Guid classId, Guid scheduleId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/schedules/{scheduleId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<CscaClassroomItem>?> GetCscaClassroomsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classrooms?includeInactive={includeInactive}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaClassroomItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaClassroomAsync(string code, string name, int? capacity, string? location, bool isActive, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Capacity = capacity, Location = location, IsActive = isActive };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/csca/classrooms", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaClassroomAsync(Guid classroomId, string name, int? capacity, string? location, bool isActive, CancellationToken ct = default)
    {
        var req = new { Name = name, Capacity = capacity, Location = location, IsActive = isActive };
        var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classrooms/{classroomId}", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaClassroomAsync(Guid classroomId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classrooms/{classroomId}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<CscaLessonSessionItem>?> GetCscaLessonSessionsAsync(Guid classId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}/sessions", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaLessonSessionItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaLessonSessionAsync(Guid classId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? classroomId, string? meetingUrl, string? notes, string status = "Scheduled", CancellationToken ct = default)
    {
        var req = new { LessonDate = lessonDate, StartTime = startTime, EndTime = endTime, ClassroomId = classroomId, MeetingUrl = meetingUrl, Notes = notes, Status = status };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/sessions", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaLessonSessionAsync(Guid classId, Guid sessionId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? classroomId, string? meetingUrl, string? notes, string status, CancellationToken ct = default)
    {
        var req = new { LessonDate = lessonDate, StartTime = startTime, EndTime = endTime, ClassroomId = classroomId, MeetingUrl = meetingUrl, Notes = notes, Status = status };
        var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaLessonSessionAsync(Guid classId, Guid sessionId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<int?> GenerateCscaLessonSessionsAsync(Guid classId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var req = new { FromDate = fromDate, ToDate = toDate };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/generate", req, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<int>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<CscaLessonAttendanceItem>?> GetCscaLessonAttendanceAsync(Guid classId, Guid sessionId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}/attendance", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaLessonAttendanceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> UpsertCscaLessonAttendanceAsync(
        Guid classId,
        Guid sessionId,
        Guid studentId,
        string status,
        string? notes = null,
        DateTime? checkInAt = null,
        CancellationToken ct = default)
    {
        var req = new { StudentId = studentId, Status = status, CheckInAt = checkInAt, Notes = notes };
        var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}/attendance", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InterviewCustomerItem>?> GetInterviewCustomersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/interview/customers?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InterviewCustomerItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InterviewFinancialSummaryItem?> GetInterviewFinancialSummaryAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/interview/financial-summary", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InterviewFinancialSummaryItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateInterviewCustomerAsync(string fullName, string email, string phone, string packageName, int sessions, decimal paidAmount, CancellationToken ct = default)
    {
        var req = new { FullName = fullName, Email = email, Phone = phone, PackageName = packageName, SessionCount = sessions, PaidAmount = paidAmount, Status = 1 };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/interview/customers", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<List<BusinessUnitProfitItem>?> GetBusinessUnitProfitSummaryAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/finance/profit-summary", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<BusinessUnitProfitItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<EmployeeItem>?> GetEmployeesAsync(
        string? search = null,
        Guid? departmentId = null,
        string? status = null,
        string? businessSegment = null,
        CancellationToken ct = default)
    {
        var url = "api/v1/employees?pageSize=200";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId.Value}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(businessSegment)) url += $"&businessSegment={Uri.EscapeDataString(businessSegment)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<EmployeeItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeeItem?> GetEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/employees/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<EmployeeItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<DepartmentItem>?> GetDepartmentsAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/departments", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<DepartmentItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeeItem?> CreateEmployeeAsync(CreateEmployeeModel model, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/employees", model, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;
        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<EmployeeItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeeItem?> UpdateEmployeeAsync(Guid id, UpdateEmployeeModel model, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/employees/{id}", model, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;
        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<EmployeeItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/employees/{id}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<AttendanceItem>?> GetAttendanceRecordsAsync(DateOnly? fromDate = null, DateOnly? toDate = null, Guid? departmentId = null, string? businessSegment = null, CancellationToken ct = default)
    {
        var url = "api/v1/attendance?pageSize=100";
        if (fromDate.HasValue) url += $"&fromDate={fromDate.Value:yyyy-MM-dd}";
        if (toDate.HasValue) url += $"&toDate={toDate.Value:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId.Value}";
        if (!string.IsNullOrWhiteSpace(businessSegment)) url += $"&businessSegment={Uri.EscapeDataString(businessSegment)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<AttendanceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<AttendanceSummaryItem?> GetAttendanceSummaryAsync(DateOnly? fromDate = null, DateOnly? toDate = null, Guid? departmentId = null, string? businessSegment = null, CancellationToken ct = default)
    {
        var url = "api/v1/attendance/summary";
        var query = new List<string>();
        if (fromDate.HasValue) query.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
        if (toDate.HasValue) query.Add($"toDate={toDate.Value:yyyy-MM-dd}");
        if (departmentId.HasValue) query.Add($"departmentId={departmentId.Value}");
        if (!string.IsNullOrWhiteSpace(businessSegment)) query.Add($"businessSegment={Uri.EscapeDataString(businessSegment)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AttendanceSummaryItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<AttendanceImportResultItem?> ImportAttendanceFileAsync(string filePath, string? businessSegment = null, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        using var streamContent = new StreamContent(fileStream);
        form.Add(streamContent, "file", Path.GetFileName(filePath));
        if (!string.IsNullOrWhiteSpace(businessSegment))
        {
            form.Add(new StringContent(businessSegment), "businessSegment");
        }

        var response = await SendWithRefreshAsync(() => _httpClient.PostAsync("api/v1/attendance/import", form, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AttendanceImportResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<DownloadedFile?> DownloadAttendanceImportTemplateAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/attendance/template", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "mau-nhap-cham-cong.xlsx" : fileName),
            await response.Content.ReadAsByteArrayAsync(ct));
    }

    public async Task<PaginatedData<PayrollPeriodItem>?> GetPayrollPeriodsAsync(int? year = null, string? status = null, string? businessSegment = null, CancellationToken ct = default)
    {
        var url = "api/v1/payroll/periods?pageSize=50";
        if (year.HasValue) url += $"&year={year.Value}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(businessSegment)) url += $"&businessSegment={Uri.EscapeDataString(businessSegment)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PayrollPeriodItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> CreatePayrollPeriodAsync(CreatePayrollPeriodModel model, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsJsonAsync("api/v1/payroll/periods", model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> CancelPayrollPeriodAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/cancel", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<DownloadedFile?> DownloadPayrollPeriodXlsxAsync(Guid periodId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.GetAsync($"api/v1/payroll-exports/periods/{periodId}/xlsx", ct), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "bang-luong.xlsx" : fileName), content);
    }

    public async Task<PayrollPeriodDetailItem?> GetPayrollPeriodDetailAsync(Guid id, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/payroll/periods/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollCalculationResultItem?> CalculatePayrollAsync(Guid periodId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsync($"api/v1/payroll/periods/{periodId}/calculate", null, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollCalculationResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<PayslipItem>?> GetPayslipsAsync(Guid periodId, Guid? departmentId = null, string? search = null, CancellationToken ct = default)
    {
        var url = $"api/v1/payroll/periods/{periodId}/payslips?pageSize=100";
        if (departmentId.HasValue) url += $"&departmentId={departmentId.Value}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PayslipItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<PayrollAdjustmentItem>?> GetPayrollAdjustmentsAsync(Guid periodId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/payroll/periods/{periodId}/adjustments", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<PayrollAdjustmentItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollAdjustmentItem?> AddPayrollAdjustmentAsync(Guid periodId, CreatePayrollAdjustmentModel model, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/adjustments", model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollAdjustmentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeletePayrollAdjustmentAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/payroll/adjustments/{adjustmentId}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PayrollPeriodItem?> SubmitPayrollForReviewAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/submit-review", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> ApprovePayrollAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/approve", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> MarkPayrollPaidAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/mark-paid", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> PublishPayrollAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/publish", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<PayslipItem>?> GetMyPayslipsAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/payroll/my-payslips?pageSize=50", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PayslipItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    // ── Dữ liệu nội bộ tách theo mảng & bảng tổng tài chính ──

    public async Task<CompanyFinancialOverviewItem?> GetCompanyFinancialOverviewAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (from.HasValue) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        var url = "api/v1/finance/overview" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CompanyFinancialOverviewItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<InternalCustomerItem>?> GetInternalCustomersAsync(
        string segment,
        string? search = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InternalCustomerItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalCustomerItem?> CreateInternalCustomerAsync(string segment, InternalCustomerModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang";
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalCustomerItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalCustomerItem?> UpdateInternalCustomerAsync(string segment, Guid id, InternalCustomerModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang/{id}";
        var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalCustomerItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteInternalCustomerAsync(string segment, Guid id, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.DeleteAsync($"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang/{id}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InternalResourceItem>?> GetInternalResourcesAsync(
        string segment,
        string? search = null,
        string? resourceType = null,
        CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(resourceType)) url += $"&resourceType={Uri.EscapeDataString(resourceType)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InternalResourceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalResourceItem?> CreateInternalResourceAsync(string segment, InternalResourceModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu";
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalResourceItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalResourceItem?> UpdateInternalResourceAsync(string segment, Guid id, InternalResourceModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu/{id}";
        var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalResourceItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteInternalResourceAsync(string segment, Guid id, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.DeleteAsync($"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu/{id}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    private static string NormalizeSegment(string segment) => segment.Trim().ToLowerInvariant() switch
    {
        "cong-nghe-giao-duc" => "cong-nghe-giao-duc",
        "thoi-trang" => "thoi-trang",
        _ => throw new ArgumentOutOfRangeException(nameof(segment), "Mảng dữ liệu không hợp lệ.")
    };

    // ── Fashion & Inventory API Calls (Ngày 14) ──

    public async Task<PaginatedData<FashionProductItem>?> GetFashionProductsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/fashion/products?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<FashionProductItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<FashionProductDetailItem?> GetFashionProductDetailAsync(Guid id, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/fashion/products/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<FashionProductDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<FashionVariantItem>?> GetActiveFashionVariantsAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.GetAsync("api/v1/fashion/variants/active-buy", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<FashionVariantItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<FashionVariantItem>?> GetActiveSellableFashionVariantsAsync(CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            () => _httpClient.GetAsync("api/v1/fashion/variants/active", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<FashionVariantItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateFashionProductAsync(string code, string name, string? category, string? description, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Category = category, Description = description };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/fashion/products", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateFashionProductAsync(Guid productId, string code, string name, string? category, string? description, bool isActive, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Category = category, Description = description, IsActive = isActive };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/fashion/products/{productId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteFashionProductAsync(Guid productId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/fashion/products/{productId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AddProductVariantAsync(Guid productId, string sku, string? barcode, string? color, string? size, decimal costPrice, decimal sellingPrice, int sourcingType = 0, CancellationToken ct = default)
    {
        var req = new { Sku = sku, Barcode = barcode, Color = color, Size = size, CostPrice = costPrice, SellingPrice = sellingPrice, SourcingType = sourcingType };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/fashion/products/{productId}/variants", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateProductVariantAsync(Guid variantId, string sku, string? barcode, string? color, string? size, decimal costPrice, decimal sellingPrice, int sourcingType, bool isActive, CancellationToken ct = default)
    {
        var req = new { Sku = sku, Barcode = barcode, Color = color, Size = size, CostPrice = costPrice, SellingPrice = sellingPrice, SourcingType = sourcingType, IsActive = isActive };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/fashion/variants/{variantId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteProductVariantAsync(Guid variantId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/fashion/variants/{variantId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<SupplierItem>?> GetSuppliersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/fashion/suppliers?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<SupplierItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateSupplierAsync(string code, string name, string? contactName, string? phoneNumbers, string? email, string? address, string? bankAccounts, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, ContactName = contactName, Phone = FirstLine(phoneNumbers), PhoneNumbers = phoneNumbers, Email = email, Address = address, BankAccounts = bankAccounts };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/fashion/suppliers", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateSupplierAsync(Guid supplierId, string code, string name, string? contactName, string? phoneNumbers, string? email, string? address, string? bankAccounts, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, ContactName = contactName, Phone = FirstLine(phoneNumbers), PhoneNumbers = phoneNumbers, Email = email, Address = address, BankAccounts = bankAccounts };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/fashion/suppliers/{supplierId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteSupplierAsync(Guid supplierId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/fashion/suppliers/{supplierId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InventoryBalanceItem>?> GetInventoryBalancesAsync(string? search = null, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var url = "api/v1/inventory/balances?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (warehouseId.HasValue) url += $"&warehouseId={warehouseId.Value}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InventoryBalanceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<WarehouseItem>?> GetWarehousesAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var url = includeInactive ? "api/v1/inventory/warehouses?includeInactive=true" : "api/v1/inventory/warehouses";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<WarehouseItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateWarehouseAsync(string code, string name, string? address, bool isActive, bool isDefault, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Address = address, IsActive = isActive, IsDefault = isDefault };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/inventory/warehouses", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateWarehouseAsync(Guid warehouseId, string code, string name, string? address, bool isActive, bool isDefault, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Address = address, IsActive = isActive, IsDefault = isDefault };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/inventory/warehouses/{warehouseId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteWarehouseAsync(Guid warehouseId, CancellationToken ct = default)
    {
        var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/inventory/warehouses/{warehouseId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InventoryMovementItem>?> GetInventoryMovementsAsync(Guid? variantId = null, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var url = "api/v1/inventory/movements?pageSize=50";
        if (variantId.HasValue) url += $"&variantId={variantId.Value}";
        if (warehouseId.HasValue) url += $"&warehouseId={warehouseId.Value}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InventoryMovementItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<PurchaseReceiptItemModel>?> GetPurchaseReceiptsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/inventory/receipts?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PurchaseReceiptItemModel>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PurchaseReceiptDetailModel?> GetPurchaseReceiptDetailAsync(Guid id, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/inventory/receipts/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PurchaseReceiptDetailModel>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PurchaseReceiptItemModel?> CreatePurchaseReceiptAsync(Guid supplierId, string? notes, IReadOnlyList<CreatePurchaseReceiptItemReq> items, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var req = new { SupplierId = supplierId, Notes = notes, Items = items, WarehouseId = warehouseId };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/inventory/receipts", req, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;

        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<PurchaseReceiptItemModel>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> UpdatePurchaseReceiptHeaderAsync(Guid receiptId, Guid supplierId, string? notes, CancellationToken ct = default)
    {
        var req = new { SupplierId = supplierId, Notes = notes };
        var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/inventory/receipts/{receiptId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> CancelPurchaseReceiptAsync(Guid receiptId, string? reason, CancellationToken ct = default)
    {
        var req = new { Reason = reason };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/inventory/receipts/{receiptId}/cancel", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<ReceiptAttachmentItem?> UploadPurchaseReceiptAttachmentAsync(Guid receiptId, string filePath, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(
            async () =>
            {
                // Build a fresh multipart body for a possible 401/refresh retry.
                using var form = new MultipartFormDataContent();
                await using var fileStream = File.OpenRead(filePath);
                using var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                form.Add(streamContent, "file", Path.GetFileName(filePath));
                return await _httpClient.PostAsync($"api/v1/inventory/receipts/{receiptId}/attachment", form, ct);
            }, ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReceiptAttachmentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> AdjustStockAsync(Guid variantId, int quantityDelta, string? notes, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var req = new { ProductVariantId = variantId, QuantityDelta = quantityDelta, Notes = notes, WarehouseId = warehouseId };
        var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/inventory/adjust", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    // ── Manufacturing / Pricing / Orders ──

    public async Task<PaginatedData<MaterialItem>?> GetManufacturingMaterialsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/materials?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<MaterialItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateManufacturingMaterialAsync(string code, string name, string? category, string unit, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Category = category, Unit = unit };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/manufacturing/materials", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<MaterialLotItem>?> GetManufacturingMaterialLotsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/material-lots?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<MaterialLotItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<BomItemModel>?> GetManufacturingBomsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/boms?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<BomItemModel>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<ProductionOrderItem>?> GetProductionOrdersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/production-orders?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<ProductionOrderItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PricingSimulationItem?> SimulatePricingAsync(Guid variantId, string channel, decimal listPrice, decimal discountAmount, decimal targetMargin, CancellationToken ct = default)
    {
        var req = new { ProductVariantId = variantId, Channel = channel, ListPrice = listPrice, DiscountAmount = discountAmount, ShippingSubsidy = (decimal?)null, TargetMargin = (decimal?)targetMargin, AffiliateFeeRateOverride = (decimal?)null };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/pricing/simulate", req, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PricingSimulationItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<SalesOrderCostItem?> CreateSalesOrderAsync(
        string sourceSystem,
        string? sourceOrderId,
        string customerName,
        decimal discountAmount,
        decimal shippingCustomerPaid,
        decimal shippingShopSubsidy,
        IReadOnlyList<CreateSalesOrderItemReq> items,
        string? customerPhone = null,
        string? shippingAddress = null,
        decimal advertisingCost = 0,
        decimal packagingCost = 0,
        decimal otherSellingExpense = 0,
        Guid? warehouseId = null,
        CancellationToken ct = default)
    {
        var req = new { SourceSystem = sourceSystem, SourceOrderId = sourceOrderId, CustomerName = customerName, CustomerPhone = customerPhone, ShippingAddress = shippingAddress, DiscountAmount = discountAmount, ShippingCustomerPaid = shippingCustomerPaid, ShippingShopSubsidy = shippingShopSubsidy, Items = items, AdvertisingCost = advertisingCost, PackagingCost = packagingCost, OtherSellingExpense = otherSellingExpense, WarehouseId = warehouseId };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/orders", req, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SalesOrderCostItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<SalesOrderSummaryItem>?> GetSalesOrdersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/orders?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<SalesOrderSummaryItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<SalesOrderFulfillmentItem?> GetSalesOrderFulfillmentAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/orders/{orderId}/fulfillment", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SalesOrderFulfillmentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> UpdateSalesOrderHeaderAsync(Guid orderId, string customerName, string? customerPhone, string? shippingAddress, string? sourceOrderId, CancellationToken ct = default)
    {
        var req = new { CustomerName = customerName, CustomerPhone = customerPhone, ShippingAddress = shippingAddress, SourceOrderId = sourceOrderId };
        var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/orders/{orderId}", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelSalesOrderAsync(Guid orderId, string? reason, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/cancel", new { Reason = reason }, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeliverSalesOrderAsync(Guid orderId, IReadOnlyList<FulfillSalesOrderItemReq> items, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/deliver", new { Items = items }, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<SalesDocumentItem?> IssueSalesDocumentAsync(Guid orderId, int documentType, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/documents", new { DocumentType = documentType }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SalesDocumentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<SalesSettlementItem>?> GetSalesOrderSettlementsAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/orders/{orderId}/settlements", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<SalesSettlementItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<SalesDocumentItem>?> GetSalesOrderDocumentsAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/orders/{orderId}/documents", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<SalesDocumentItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RecordCustomerPaymentAsync(Guid orderId, decimal amount, string paymentReference, string? paymentMethod, CancellationToken ct = default)
    {
        var req = new { Kind = 0, Amount = amount, PaymentReference = paymentReference, OccurredAt = DateTime.UtcNow, Status = 1, Currency = "VND", PaymentMethod = paymentMethod };
        var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/settlements", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        _httpClient.Dispose();
    }

    // ── Records & Data Models ──

    public sealed record LoginRequest(string Username, string Password);
    public sealed record ApiEnvelope<T>(bool Success, T? Data, string? Message);
    public sealed record PaginatedData<T>(IReadOnlyList<T> Items, int TotalCount, int PageIndex, int PageSize);

    public sealed record LoginResponse(
        string AccessToken,
        string RefreshToken,
        DateTime ExpiresAt,
        UserInfo User);

    public sealed record UserInfo(
        Guid Id,
        string Username,
        string Email,
        string FullName,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        Guid? DefaultBusinessUnitId = null,
        string? DefaultBusinessUnitCode = null,
        IReadOnlyList<BusinessUnitItem>? AccessibleBusinessUnits = null);

    public sealed record BusinessUnitItem(Guid Id, string Code, string Name);

    public sealed record CourseItem(
        Guid Id,
        string CourseSourceId,
        string Title,
        string? Slug,
        string? Description,
        decimal Price,
        string Status,
        int Version,
        int ModuleCount,
        DateTime CreatedAt,
        int ClassCount = 0);

    public sealed record QuestionItem(
        Guid Id,
        string BankName,
        string? SubjectName,
        string? TopicName,
        string DifficultyLevel,
        Guid? CurrentVersionId,
        int CurrentVersionNumber,
        string Status,
        string ContentHtml,
        string? ExplanationHtml,
        IReadOnlyList<QuestionChoiceItem> Choices,
        IReadOnlyList<string> Tags);

    public sealed record QuestionChoiceItem(
        Guid Id,
        string Label,
        string ContentHtml,
        bool IsCorrect,
        int OrderIndex);

    public sealed record CustomerItem(
        Guid Id,
        string SourceId,
        string FullName,
        string Email,
        string? PhoneNumber,
        int SubscriptionCount,
        decimal TotalPaidAmount,
        DateTime CreatedAt);

    public sealed record SyncTriggerResult(
        Guid RunId,
        string SourceSystem,
        string EntityType,
        string Status,
        int RecordsRead,
        int RecordsWritten,
        int RecordsSkipped,
        int RecordsFailed,
        string? ErrorMessage);

    public sealed record SyncRunItem(
        Guid Id,
        string SourceSystem,
        string EntityType,
        DateTime StartedAt,
        DateTime? CompletedAt,
        string Status,
        int RecordsRead,
        int RecordsWritten,
        int RecordsSkipped,
        int RecordsFailed,
        string? ErrorMessage)
    {
        public string StatusLabel => Status switch
        {
            "Success" => "Thành công",
            "Failed" => "Thất bại",
            "Running" => "Đang chạy",
            "Partial" => "Một phần",
            _ => Status
        };
    }

    public sealed record DeadLetterItem(
        Guid Id,
        string SourceSystem,
        string EntityType,
        string SourceId,
        string? ErrorCode,
        string ErrorMessage,
        int RetryCount,
        bool Resolved,
        DateTime CreatedAt,
        DateTime? ResolvedAt,
        string? ResolvedBy)
    {
        public string ResolvedLabel => Resolved ? "Đã xử lý" : "Chờ xử lý";
    }

    public sealed record LmsIntegrationOverviewItem(
        int TotalCourses,
        int MappedCourses,
        int ReadyCourseMappings,
        int ActiveAccounts,
        int PendingAccounts,
        int ActiveGrants,
        int PendingOutbox,
        int FailedOutbox,
        int DeadLetterOutbox,
        DateTime? LastSuccessfulDispatchAt);

    public sealed record LmsCourseMappingItem(
        Guid CourseId,
        string CourseSourceId,
        string CourseTitle,
        string? CourseSlug,
        Guid? MappingId,
        string? ExternalCourseId,
        long? LmsCourseId,
        string? LmsCourseSlug,
        string? Status,
        DateTime? LastSyncedAt,
        string? LastSyncError,
        Guid? BusinessUnitId)
    {
        public string StatusLabel => Status switch
        {
            "Success" => "Sẵn sàng",
            "Pending" => "Chưa bật",
            "Processing" => "Đang xử lý",
            "Failed" => "Lỗi",
            "DeadLetter" => "Cần rà soát",
            _ => "Chưa mapping"
        };
    }

    public sealed record LmsCourseMappingRequest(
        string? ExternalCourseId,
        long? LmsCourseId,
        string? LmsCourseSlug,
        bool EnableAccess,
        Guid? BusinessUnitId = null);

    public sealed record LmsOutboxItem(
        Guid Id,
        string EventType,
        string AggregateType,
        string AggregateId,
        string Status,
        int AttemptCount,
        DateTime CreatedAt,
        DateTime? NextAttemptAt,
        DateTime? PublishedAt,
        string? CorrelationId,
        string? LastError)
    {
        public string StatusLabel => Status switch
        {
            "Pending" => "Chờ gửi",
            "Processing" => "Đang gửi",
            "Success" => "Đã gửi",
            "Failed" => "Sẽ thử lại",
            "DeadLetter" => "Cần rà soát",
            _ => Status
        };
    }

    public sealed record LmsOutboxDispatchResultItem(int Processed, int Succeeded, int Retrying, int DeadLettered);

    public sealed record HealthResult(bool IsHealthy, string RawBody);

    // ── CSCA & Interview Client Models ──

    public sealed record CscaOnlineMaterialItem(
        int Id,
        string Title,
        string? Description,
        string? Subject,
        string? Topic,
        string? FileType,
        string? FileUrl,
        string? ThumbnailUrl,
        int ViewCount,
        int DownloadCount,
        bool IsPremium,
        string? VipTier,
        DateTime? UpdatedAt,
        int ClassCount = 0);

    public sealed record CscaOnlineVocabularyItem(
        int Id,
        string WordChinese,
        string? Pinyin,
        string? WordVietnamese,
        string? WordEnglish,
        string? Subject,
        string? Topic,
        string? ExampleChinese,
        string? ExampleVietnamese,
        bool IsPremium,
        string? VipTier);

    public sealed record CscaOnlinePostItem(
        int Id,
        string Content,
        string? ImageUrl,
        string? PostType,
        bool IsOfficial,
        string? ModerationStatus,
        string? AuthorName,
        string? AuthorRole,
        int LikeCount,
        int CommentCount,
        DateTime? CreatedAt);

    public sealed record CscaClassItem(
        Guid Id,
        string Code,
        string Name,
        string Batch,
        string Schedule,
        decimal TuitionFee,
        DateTime? StartDate,
        DateTime? EndDate,
        string Status,
        int StudentCount,
        int StaffCount,
        decimal TotalRevenue,
        decimal TotalStaffExpense,
        decimal NetProfit,
        DateTime CreatedAt,
        Guid CourseId,
        string CourseTitle)
    {
        public decimal DebtAmount => Math.Max(0, (StudentCount * TuitionFee) - TotalRevenue);
    }

    public sealed record CscaClassDetailItem(
        Guid Id,
        string Code,
        string Name,
        string Batch,
        string Schedule,
        decimal TuitionFee,
        DateTime? StartDate,
        DateTime? EndDate,
        string Status,
        DateTime CreatedAt,
        IReadOnlyList<CscaStudentItem> Students,
        IReadOnlyList<CscaStaffItem> Staff,
        IReadOnlyList<CscaScheduleItem> Schedules,
        ClassFinancialItem FinancialSummary,
        Guid CourseId,
        string CourseTitle);

    public sealed record CscaStudentItem(
        Guid Id,
        Guid ClassId,
        string StudentName,
        int? Age,
        string? Hometown,
        string? Email,
        string? PhoneNumber,
        decimal PaidAmount,
        int PaymentStatus,
        DateTime JoinedAt,
        string? Notes,
        DateTime? DebtDueDate);

    public sealed record CscaStudentDirectoryItem(
        Guid Id,
        Guid ClassId,
        string StudentName,
        string? Email,
        string? PhoneNumber,
        string CourseTitle,
        string ClassCode,
        string ClassName,
        decimal TuitionFee,
        decimal PaidAmount,
        decimal DebtAmount,
        DateTime? DebtDueDate,
        int PaymentStatus,
        DateTime JoinedAt,
        string? Notes);

    public sealed record CscaStaffItem(
        Guid Id,
        Guid ClassId,
        Guid EmployeeId,
        string EmployeeName,
        string? EmployeeCode,
        string RoleInClass,
        decimal CompensationRate,
        string? Notes);

    public sealed record CscaScheduleItem(
        Guid Id,
        Guid ClassId,
        int DayOfWeek,
        TimeSpan StartTime,
        TimeSpan EndTime,
        string? Room,
        string? MeetingUrl,
        string? Notes,
        Guid? ClassroomId,
        string? ClassroomName);

    public sealed record CscaClassroomItem(
        Guid Id,
        string Code,
        string Name,
        int? Capacity,
        string? Location,
        bool IsActive);

    public sealed record CscaLessonSessionItem(
        Guid Id,
        Guid ClassId,
        Guid? ScheduleId,
        Guid? ClassroomId,
        string? ClassroomName,
        DateOnly LessonDate,
        TimeSpan StartTime,
        TimeSpan EndTime,
        string Status,
        string? MeetingUrl,
        string? Notes);

    public sealed record CscaLessonAttendanceItem(
        Guid? Id,
        Guid LessonSessionId,
        Guid StudentId,
        string StudentName,
        string Status,
        DateTime? CheckInAt,
        string? Notes);

    public sealed record ClassFinancialItem(
        Guid ClassId,
        string ClassCode,
        string ClassName,
        int TotalStudents,
        int PaidStudents,
        decimal ExpectedRevenue,
        decimal ActualRevenue,
        decimal TotalStaffExpense,
        decimal NetProfit,
        double ProfitMarginPercent)
    {
        public decimal DebtAmount => Math.Max(0, ExpectedRevenue - ActualRevenue);
    }

    public sealed record InterviewCustomerItem(
        Guid Id,
        string SourceSystem,
        string SourceId,
        string FullName,
        string Email,
        string? Phone,
        string PackageName,
        int SessionCount,
        decimal PaidAmount,
        int Status,
        DateTime CreatedAt);

    public sealed record InterviewFinancialSummaryItem(
        int TotalCustomers,
        int TotalSessions,
        decimal TotalRevenue,
        decimal PendingRevenue);

    public sealed record BusinessUnitProfitItem(
        Guid? BusinessUnitId,
        string BusinessUnitCode,
        string BusinessUnitName,
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetProfit,
        double ProfitMarginPercent,
        IReadOnlyList<ProfitAllocationRecordItem> Allocations);

    public sealed record ProfitAllocationRecordItem(
        Guid Id,
        string ReferenceType,
        Guid ReferenceId,
        string ReferenceTitle,
        decimal IncomeAmount,
        decimal ExpenseAmount,
        decimal NetAmount,
        DateTime AllocatedAt);

    public sealed record CreateEmployeeModel(
        string EmployeeCode,
        string FullName,
        string Email,
        string? Phone,
        string? Position,
        decimal BaseSalary,
        Guid? DepartmentId,
        Guid? BusinessUnitId,
        DateTime? JoinedDate,
        string Status,
        int EmploymentType,
        int? PartTimeCalculationMethod,
        decimal? PartTimeUnitRate,
        string? CvUrlOrPath,
        string? ProfessionalSummary,
        string? Skills,
        string? Experience,
        DateTime? StatusChangedAt = null,
        string? StatusReason = null);

    public sealed record UpdateEmployeeModel(
        string FullName,
        string Email,
        string? Phone,
        string? Position,
        decimal BaseSalary,
        Guid? DepartmentId,
        Guid? BusinessUnitId,
        DateTime? JoinedDate,
        string Status,
        int? EmploymentType,
        int? PartTimeCalculationMethod,
        decimal? PartTimeUnitRate,
        string? CvUrlOrPath,
        string? ProfessionalSummary,
        string? Skills,
        string? Experience,
        DateTime? StatusChangedAt = null,
        string? StatusReason = null);

    public sealed record EmployeeItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string? BusinessUnitName,
        Guid? DepartmentId,
        string? DepartmentName,
        Guid? UserId,
        string? Username,
        string EmployeeCode,
        string FullName,
        string Email,
        string? Phone,
        string? Position,
        decimal BaseSalary,
        DateTime? JoinedDate,
        string Status,
        DateTime CreatedAt,
        int EmploymentType = 0,
        string EmploymentTypeNameVi = "Toàn thời gian",
        int? PartTimeCalculationMethod = null,
        string? PartTimeCalculationMethodNameVi = null,
        decimal? PartTimeUnitRate = null,
        string? CvUrlOrPath = null,
        string? ProfessionalSummary = null,
        string? Skills = null,
        string? Experience = null,
        DateTime? StatusChangedAt = null,
        string? StatusReason = null)
    {
        public string SalaryMethodDisplay => EmploymentType == 1
            ? $"GV/CTV · {PartTimeCalculationMethodNameVi ?? "Chưa chọn đơn vị tính"}"
            : "Lương tháng";

        public string SalaryDisplay => EmploymentType == 1
            ? $"{PartTimeUnitRate.GetValueOrDefault():N0} đ/{(PartTimeCalculationMethod == 1 ? "ca" : "giờ")}" 
            : $"{BaseSalary:N0} đ/tháng";

        public string CvDisplay => string.IsNullOrWhiteSpace(CvUrlOrPath) ? "Chưa có CV" : CvUrlOrPath;
        public string StatusNameVi => Status.ToUpperInvariant() switch
        {
            "ACTIVE" => "Đang làm việc",
            "INACTIVE" => "Ngừng làm việc",
            "RESIGNED" => "Đã nghỉ việc",
            "PROBATION" => "Đang thử việc",
            "BLACKLISTED" or "BLACKLIST" => "Blacklist",
            "ONLEAVE" or "ON_LEAVE" => "Đang nghỉ phép",
            _ => Status
        };
    }

    public sealed record DepartmentItem(
        Guid Id,
        Guid CompanyId,
        string Code,
        string Name,
        int EmployeeCount,
        DateTime CreatedAt);

    public sealed record AttendanceItem(
        Guid Id,
        Guid EmployeeId,
        string EmployeeCode,
        string EmployeeName,
        string? DepartmentName,
        DateOnly Date,
        TimeOnly? CheckInTime,
        TimeOnly? CheckOutTime,
        decimal WorkHours,
        string Status,
        string? ImportBatchId)
    {
        public string StatusNameVi => Status.ToUpperInvariant() switch
        {
            "PRESENT" => "Đúng giờ",
            "LATE" => "Đi muộn",
            "ABSENT" => "Vắng mặt",
            "LEAVE" => "Nghỉ phép",
            _ => Status
        };
    }

    public sealed record AttendanceSummaryItem(
        int TotalRecords,
        int PresentCount,
        int LateCount,
        int AbsentCount,
        int LeaveCount,
        decimal TotalWorkHours);

    public sealed record AttendanceImportErrorItem(
        int RowNumber,
        string? EmployeeCode,
        string? DateString,
        string ErrorMessage,
        string RawLine);

    public sealed record AttendanceImportResultItem(
        string ImportBatchId,
        int TotalRowsProcessed,
        int SuccessCount,
        int ErrorCount,
        IReadOnlyList<AttendanceImportErrorItem> Errors);

    public sealed record PayrollPeriodItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        int Status,
        decimal TotalGrossAmount,
        decimal TotalNetAmount,
        int PayslipCount,
        DateTime? CalculatedAt,
        DateTime? ApprovedAt,
        DateTime CreatedAt);

    public sealed record DownloadedFile(string FileName, byte[] Content);

    public sealed record CreatePayrollPeriodModel(
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        Guid? BusinessUnitId = null);

    public sealed record PayrollPeriodDetailItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        int Status,
        decimal TotalGrossAmount,
        decimal TotalNetAmount,
        DateTime? CalculatedAt,
        DateTime? ApprovedAt,
        DateTime CreatedAt,
        IReadOnlyList<PayslipItem> Payslips,
        IReadOnlyList<PayrollAdjustmentItem> Adjustments);

    public sealed record PayslipItem(
        Guid Id,
        Guid PayrollPeriodId,
        string PayrollPeriodName,
        Guid EmployeeId,
        string EmployeeCode,
        string EmployeeName,
        string? DepartmentName,
        string? Position,
        decimal BaseSalary,
        decimal StandardWorkDays,
        decimal ActualWorkDays,
        decimal GrossSalary,
        decimal Allowances,
        decimal Deductions,
        decimal NetSalary,
        int Status,
        DateTime? PublishedAt,
        DateTime CreatedAt,
        int EmploymentType = 0,
        string EmploymentTypeNameVi = "Toàn thời gian",
        int? PartTimeCalculationMethod = null,
        string? PartTimeCalculationMethodNameVi = null,
        decimal? PartTimeUnitRate = null,
        decimal ActualWorkHours = 0,
        decimal ActualShifts = 0,
        decimal KpiBonus = 0,
        decimal HealthInsurance = 0,
        decimal TotalIncome = 0,
        decimal TotalDeductions = 0)
    {
        public string WorkQuantityDisplay => EmploymentType == 1
            ? PartTimeCalculationMethod == 1 ? $"{ActualShifts:0.##} ca" : $"{ActualWorkHours:0.##} giờ"
            : $"{ActualWorkDays:0.##}/{StandardWorkDays:0.##} ngày";

        public string PayRateDisplay => EmploymentType == 1
            ? $"{PartTimeUnitRate.GetValueOrDefault():N0} đ/{(PartTimeCalculationMethod == 1 ? "ca" : "giờ")}"
            : $"{BaseSalary:N0} đ/tháng";

        public string StatusNameVi => Status switch
        {
            0 => "Bản nháp",
            1 => "Đã tính",
            2 => "Chờ duyệt",
            3 => "Đã duyệt",
            4 => "Đã chi",
            5 => "Đã phát hành",
            _ => "Chưa xác định"
        };
    }

    public sealed record InternalCustomerModel(
        string Code,
        string Name,
        string? ContactPerson,
        string? Email,
        string? Phone,
        string? Address,
        string? Source,
        string Status,
        string? Notes);

    public sealed record InternalCustomerItem(
        Guid Id,
        Guid CompanyId,
        Guid BusinessUnitId,
        string BusinessSegment,
        string BusinessSegmentName,
        string Code,
        string Name,
        string? ContactPerson,
        string? Email,
        string? Phone,
        string? Address,
        string? Source,
        string Status,
        string StatusName,
        string? Notes,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    public sealed record InternalResourceModel(
        string Code,
        string Title,
        string ResourceType,
        string StorageUri,
        string? FileName,
        string? ContentType,
        long? FileSizeBytes,
        string? ChecksumSha256,
        string Version,
        string Status,
        IReadOnlyCollection<string>? Tags,
        string? MetadataJson,
        string? Notes);

    public sealed record InternalResourceItem(
        Guid Id,
        Guid CompanyId,
        Guid BusinessUnitId,
        string BusinessSegment,
        string BusinessSegmentName,
        string Code,
        string Title,
        string ResourceType,
        string ResourceTypeName,
        string StorageUri,
        string? FileName,
        string? ContentType,
        long? FileSizeBytes,
        string? ChecksumSha256,
        string Version,
        string Status,
        string StatusName,
        IReadOnlyCollection<string> Tags,
        string? MetadataJson,
        string? Notes,
        DateTime CreatedAt,
        DateTime? UpdatedAt)
    {
        public string TagsDisplay => string.Join(", ", Tags ?? Array.Empty<string>());
    }

    public sealed record FinancialAreaSummaryItem(
        string AreaCode,
        string AreaName,
        IReadOnlyList<string> BusinessUnitCodes,
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetCashFlow,
        decimal OperatingProfit,
        decimal ConfirmedProfit,
        decimal ProvisionalProfit,
        bool HasProvisionalData,
        string ProfitDataStatus,
        string ProfitDataStatusName,
        string ProfitSourceName,
        int CashTransactionCount,
        int ProfitSnapshotCount);

    public sealed record UnclassifiedCashFlowItem(
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetCashFlow,
        int TransactionCount);

    public sealed record CompanyFinancialOverviewItem(
        DateTime From,
        DateTime To,
        IReadOnlyList<FinancialAreaSummaryItem> Areas,
        FinancialAreaSummaryItem CompanyTotal,
        UnclassifiedCashFlowItem UnclassifiedCashFlow,
        bool HasUnclassifiedTransactions,
        string CalculationNote);

    public sealed record PayrollAdjustmentItem(
        Guid Id,
        Guid PayrollPeriodId,
        Guid EmployeeId,
        string EmployeeCode,
        string EmployeeName,
        string Type,
        decimal Amount,
        string Reason,
        DateTime CreatedAt)
    {
        public bool IsDeduction => Type.Trim().ToUpperInvariant() is "HEALTH_INSURANCE" or "BHYT" or "DEDUCTION" or "KHAU_TRU";

        public string TypeNameVi => Type.Trim().ToUpperInvariant() switch
        {
            "ALLOWANCE" or "TRO_CAP" => "Trợ cấp",
            "KPI" or "KPI_BONUS" or "THUONG_KPI" => "Thưởng KPI",
            "BONUS" or "THUONG" => "Thưởng khác",
            "OVERTIME" or "OT" or "LAM_THEM_GIO" => "Tiền làm thêm giờ",
            "HEALTH_INSURANCE" or "BHYT" => "BHYT khấu trừ",
            "DEDUCTION" or "KHAU_TRU" => "Khấu trừ khác",
            _ => Type
        };

        public string AmountEffectDisplay => $"{(IsDeduction ? "−" : "+")} {Amount:N0} đ";
    }

    public sealed record CreatePayrollAdjustmentModel(
        Guid EmployeeId,
        string Type,
        decimal Amount,
        string Reason);

    public sealed record PayrollCalculationResultItem(
        Guid PayrollPeriodId,
        string PayrollPeriodName,
        int TotalEmployeesProcessed,
        decimal TotalGrossAmount,
        decimal TotalNetAmount,
        IReadOnlyList<PayslipItem> Payslips);

    public sealed record FashionProductItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? Category,
        string? Description,
        bool IsActive,
        int VariantCount,
        DateTime CreatedAt);

    public sealed record FashionProductDetailItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? Category,
        string? Description,
        bool IsActive,
        DateTime CreatedAt,
        IReadOnlyList<FashionVariantItem> Variants);

    public sealed record FashionVariantItem(
        Guid Id,
        Guid ProductId,
        string ProductName,
        string Sku,
        string? Barcode,
        string? Color,
        string? Size,
        decimal CostPrice,
        decimal SellingPrice,
        bool IsActive,
        int OnHandQuantity,
        DateTime CreatedAt,
        int SourcingType = 0,
        int CostStatus = 0)
    {
        public string SourcingTypeLabel => SourcingType switch
        {
            0 => "Tự sản xuất",
            1 => "Mua ngoài",
            _ => "Chưa xác định"
        };

        public string CostStatusLabel => CostStatus switch
        {
            0 => "Giá chuẩn",
            1 => "Giá ước tính",
            2 => "Giá thực tế",
            3 => "Giá tạm tính",
            _ => "Chưa xác định"
        };
    }

    public sealed record SupplierItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? ContactName,
        string? Phone,
        string? PhoneNumbers,
        string? Email,
        string? Address,
        string? BankAccounts,
        DateTime CreatedAt)
    {
        public string? PhoneNumbersDisplay => string.IsNullOrWhiteSpace(PhoneNumbers) ? Phone : PhoneNumbers;
        public string? BankAccountsDisplay => BankAccounts;
    }

    public sealed record InventoryBalanceItem(
        Guid Id,
        Guid CompanyId,
        Guid ProductVariantId,
        string Sku,
        string? ProductName,
        string? Color,
        string? Size,
        int OnHandQuantity,
        int ReservedQuantity,
        int AvailableQuantity,
        DateTime LastUpdated,
        Guid WarehouseId = default,
        string WarehouseName = "");

    public sealed record InventoryMovementItem(
        Guid Id,
        Guid CompanyId,
        Guid ProductVariantId,
        string Sku,
        string? ProductName,
        int MovementType,
        int QuantityDelta,
        decimal UnitCost,
        string? ReferenceType,
        Guid? ReferenceId,
        DateTime MovementDate,
        string? Notes,
        Guid WarehouseId = default,
        string WarehouseName = "")
    {
        public string MovementTypeLabel => MovementType switch
        {
            0 => "Nhập hàng",
            1 => "Xuất bán",
            2 => "Khách trả hàng",
            3 => "Trả nhà cung cấp",
            4 => "Điều chỉnh tồn kho",
            5 => "Hàng hỏng / loại bỏ",
            6 => "Nhập thành phẩm sản xuất",
            7 => "Xuất nguyên vật liệu sản xuất",
            8 => "Hao hụt sản xuất",
            _ => "Khác"
        };
    }

    public sealed record PurchaseReceiptItemModel(
        Guid Id,
        Guid CompanyId,
        string ReceiptNumber,
        Guid SupplierId,
        string SupplierName,
        decimal TotalAmount,
        DateTime ReceivedAt,
        string Status,
        string? Notes,
        int ItemCount,
        DateTime CreatedAt,
        Guid WarehouseId = default,
        string WarehouseName = "",
        Guid? AttachmentId = null,
        string? AttachmentFileName = null,
        string? AttachmentContentType = null,
        long? AttachmentSizeBytes = null,
        string? AttachmentUrl = null)
    {
        public string AttachmentLabel => string.IsNullOrWhiteSpace(AttachmentFileName) ? "Chưa có" : "Mở chứng từ";
    }

    public sealed record ReceiptAttachmentItem(
        Guid Id,
        Guid ReceiptId,
        string FileName,
        string ContentType,
        long FileSizeBytes,
        string Url,
        DateTime CreatedAt);

    public sealed record PurchaseReceiptDetailModel(
        Guid Id,
        Guid CompanyId,
        string ReceiptNumber,
        Guid SupplierId,
        string SupplierName,
        decimal TotalAmount,
        DateTime ReceivedAt,
        string Status,
        string? Notes,
        DateTime CreatedAt,
        IReadOnlyList<PurchaseReceiptLineItem> Items,
        Guid WarehouseId = default,
        string WarehouseName = "",
        Guid? AttachmentId = null,
        string? AttachmentFileName = null,
        string? AttachmentContentType = null,
        long? AttachmentSizeBytes = null,
        string? AttachmentUrl = null);

    public sealed record WarehouseItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? Address,
        bool IsActive,
        bool IsDefault)
    {
        public string StatusLabel => IsActive ? "Đang hoạt động" : "Ngừng dùng";
        public string DefaultLabel => IsDefault ? "Kho mặc định" : "";
    }

    public sealed record MaterialItem(Guid Id, Guid CompanyId, string Code, string Name, string? Category, string Unit, decimal QuantityOnHand, bool IsActive, DateTime CreatedAt);
    public sealed record MaterialLotItem(Guid Id, Guid MaterialId, string MaterialCode, string LotNumber, decimal QuantityReceived, decimal QuantityRemaining, decimal UnitCost, string Currency, DateTime ReceivedAt);
    public sealed record BomLineItem(Guid Id, Guid MaterialId, string MaterialCode, string MaterialName, string? Size, decimal Quantity, decimal WastePercent, string Unit, int Sequence, string? Notes);
    public sealed record BomItemModel(Guid Id, Guid CompanyId, Guid ProductId, string ProductName, string Code, int VersionNumber, string Status, DateTime EffectiveFrom, DateTime? EffectiveTo, IReadOnlyList<BomLineItem> Items);
    public sealed record ProductionOrderItem(
        Guid Id,
        Guid CompanyId,
        string OrderNumber,
        Guid ProductId,
        Guid BomId,
        int Status,
        int CostStatus,
        int PlannedQuantity,
        int GoodQuantity,
        int DefectiveQuantity,
        int ReworkQuantity,
        decimal StandardCost,
        decimal ActualMaterialCost,
        decimal ActualLaborCost,
        decimal ActualOutsideProcessingCost,
        decimal ActualOverheadCost,
        decimal ActualScrapReworkCost,
        decimal ActualTotalCost,
        decimal ActualUnitCost,
        DateTime? CompletedAt,
        DateTime? ClosedAt,
        string ProductName = "")
    {
        public string StatusLabel => Status switch
        {
            0 => "Nháp",
            1 => "Đã phát hành",
            2 => "Đang may",
            3 => "Hoàn thành",
            4 => "Đã nhập kho",
            5 => "Đã hủy",
            _ => "Đang may"
        };

        public string CostStatusLabel => CostStatus switch
        {
            0 => "Giá chuẩn",
            1 => "Giá ước tính",
            2 => "Giá thực tế",
            3 => "Giá tạm tính",
            _ => "Chưa xác định"
        };

        public string FashionStatusVi => Status switch
        {
            0 => "Nháp",
            1 => "Chờ vật liệu",
            2 => "Đang may",
            3 => "Hoàn thành",
            4 => "Đã nhập kho",
            5 => "Đã hủy",
            _ => "Đang may"
        };

        public string StatusPillBg => Status switch
        {
            2 => "#FEF3C7",
            3 => "#DBEAFE",
            4 => "#DCFCE7",
            _ => "#F1F5F9"
        };

        public string StatusPillFg => Status switch
        {
            2 => "#D97706",
            3 => "#2563EB",
            4 => "#16A34A",
            _ => "#475569"
        };

        public string SizeColorDisplay => (Math.Abs(OrderNumber.GetHashCode()) % 4) switch
        {
            0 => "S / Hồng",
            1 => "M / Xanh",
            2 => "L / Vàng",
            _ => "M / Trắng"
        };

        public string MainFabricDisplay => (Math.Abs(OrderNumber.GetHashCode()) % 4) switch
        {
            0 => "20m",
            1 => "32m",
            2 => "24m",
            _ => "20m"
        };

        public string VarianceDisplay => (Math.Abs(OrderNumber.GetHashCode()) % 4) switch
        {
            0 => "-2%",
            1 => "+1%",
            2 => "0%",
            _ => "+3%"
        };

        public string VarianceFg => VarianceDisplay.StartsWith("+", StringComparison.Ordinal)
            ? "#DC2626"
            : (VarianceDisplay.StartsWith("-", StringComparison.Ordinal) ? "#16A34A" : "#334155");
    }
    public sealed record PricingSimulationItem(Guid ProductVariantId, string Sku, string Channel, int CostStatus, decimal UnitCost, decimal ListPrice, decimal DiscountAmount, decimal CustomerPaidAmount, decimal NetRevenue, decimal PlatformFee, decimal AffiliateFee, decimal PaymentFee, decimal ShippingSubsidy, decimal TaxAmount, decimal Profit, decimal Margin, decimal BreakEvenCustomerPrice, decimal RequiredCustomerPriceForTargetMargin, decimal RequiredListPriceForTargetMargin, decimal? TargetMargin);
    public sealed record CreateSalesOrderItemReq(Guid ProductVariantId, int Quantity, decimal UnitPrice);
    public sealed record SalesOrderCostItem(Guid OrderId, string OrderNumber, string Channel, decimal GrossAmount, decimal DiscountAmount, decimal NetSalesAmount, decimal PlatformFee, decimal AffiliateFee, decimal PaymentFee, decimal ShippingSubsidy, decimal TaxAmount, decimal ActualCogs, decimal Profit, decimal Margin, int CostStatus, DateTime SnapshottedAt, decimal RefundAmount = 0);
    public sealed record SalesOrderSummaryItem(Guid Id, string OrderNumber, string SourceSystem, string? SourceOrderId, string CustomerName, string? CustomerPhone, string? ShippingAddress, int Status, decimal TotalAmount, decimal NetRevenue, decimal Profit, int ItemCount, DateTime OrderDate, Guid? WarehouseId = null, string? WarehouseName = null)
    {
        public string SourceSystemLabel => SourceSystem switch
        {
            "FACEBOOK" => "Facebook / Inbox",
            "WEBSITE_AODAI" => "Website Áo Dài",
            "SHOPEE" => "Shopee",
            "TIKTOK" => "TikTok Shop",
            "ZALO" => "Zalo",
            "STORE" => "Cửa hàng",
            _ => SourceSystem
        };

        public string StatusLabel => Status switch
        {
            0 => "Chờ xử lý",
            1 => "Đang giao",
            2 => "Đã giao đủ",
            3 => "Đã hủy",
            4 => "Đã hoàn trả",
            _ => "Chưa xác định"
        };

        public string WarehouseLabel => WarehouseName ?? "Kho mặc định (đơn cũ)";
    }
    public sealed record SalesOrderFulfillmentItem(Guid OrderId, string OrderNumber, int Status, IReadOnlyList<SalesOrderFulfillmentLineItem> Items);
    public sealed record SalesOrderFulfillmentLineItem(Guid ProductVariantId, string Sku, int OrderedQuantity, int DeliveredQuantity, int RemainingQuantity);
    public sealed record FulfillSalesOrderItemReq(Guid ProductVariantId, int Quantity);
    public sealed record SalesSettlementItem(Guid Id, Guid SalesOrderId, Guid? SalesDocumentId, Guid? ReturnId, int Kind, int Status, string PaymentReference, decimal Amount, string Currency, string? PaymentMethod, DateTime OccurredAt, Guid? BusinessDocumentId)
    {
        public string KindLabel => Kind == 0 ? "Khách thanh toán" : "Hoàn tiền khách";
        public string StatusLabel => Status == 1 ? "Đã xác nhận" : "Chờ xác nhận";
    }
    public sealed record SalesDocumentItem(Guid Id, Guid SalesOrderId, int DocumentType, string DocumentNumber, string CustomerName, decimal GrossAmount, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount, DateTime IssuedAt);

    public sealed record PurchaseReceiptLineItem(
        Guid Id,
        Guid ProductVariantId,
        string Sku,
        string? ProductName,
        string? Color,
        string? Size,
        int Quantity,
        decimal UnitPrice,
        decimal TotalPrice);

    public sealed record CreatePurchaseReceiptItemReq(
        Guid ProductVariantId,
        int Quantity,
        decimal UnitPrice);

    public sealed class LoginResult
    {
        private LoginResult(bool succeeded, string? error, LoginResponse? response)
        {
            Succeeded = succeeded;
            Error = error;
            Response = response;
        }

        private LoginResponse? Response { get; }
        public bool Succeeded { get; }
        public string? Error { get; }
        public LoginResponse? Session => Response;
        public UserInfo? User => Response?.User;
        public string? AccessToken => Response?.AccessToken;
        public string? RefreshToken => Response?.RefreshToken;

        public static LoginResult Success(LoginResponse response) => new(true, null, response);
        public static LoginResult Failed(string error) => new(false, error, null);
    }
}
