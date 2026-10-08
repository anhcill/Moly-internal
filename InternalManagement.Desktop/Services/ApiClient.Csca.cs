using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── CSCA & Interview API Calls (Ngày 10) ──

    public async Task<PaginatedData<CscaClassItem>?> GetCscaClassesAsync(string? search = null, string? batch = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/classes?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(batch)) url += $"&batch={Uri.EscapeDataString(batch)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CscaClassItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaStudentDirectoryItem>?> GetCscaStudentDirectoryAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/students";
        if (!string.IsNullOrWhiteSpace(search)) url += $"?search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaStudentDirectoryItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlineMaterialItem>?> GetCscaOnlineMaterialsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/materials?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlineMaterialItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlineVocabularyItem>?> GetCscaOnlineVocabularyAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/vocabulary?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlineVocabularyItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlinePostItem>?> GetCscaOnlinePostsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/posts?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlinePostItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<CscaClassDetailItem?> GetCscaClassDetailAsync(Guid classId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CscaClassDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaClassAsync(string code, string name, string batch, string schedule, decimal tuitionFee, Guid? courseId = null, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Batch = batch, Schedule = schedule, TuitionFee = tuitionFee, CourseId = courseId, Status = "Active" };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/csca/classes", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> EnrollStudentAsync(Guid classId, string studentName, string email, string phone, decimal paidAmount, CancellationToken ct = default)
    {
        var req = new { StudentName = studentName, Email = email, PhoneNumber = phone, PaidAmount = paidAmount, PaymentStatus = paidAmount > 0 ? 1 : 0 };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/students", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    private async Task<string?> TryExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msgElem) && msgElem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var msg = msgElem.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
            }
            if (root.TryGetProperty("Message", out var msgElemCap) && msgElemCap.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var msg = msgElemCap.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
            }
            if (root.TryGetProperty("errors", out var errElem) || root.TryGetProperty("Errors", out errElem))
            {
                if (errElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var list = errElem.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (list.Count > 0) return string.Join("\n", list);
                }
                else if (errElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var list = errElem.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString())).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (list.Count > 0) return string.Join("\n", list);
                }
            }
            if (root.TryGetProperty("title", out var titleElem) && titleElem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return titleElem.GetString();
            }
        }
        catch
        {
        }
        return null;
    }

    public async Task<bool> EnrollCscaStudentAsync(
        Guid classId, string studentName, string email, string phone, int? age,
        string hometown, decimal paidAmount, int paymentStatus, string notes, DateTime? debtDueDate = null,
        decimal discountAmount = 0, string? discountNote = null, CancellationToken ct = default)
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
            DiscountAmount = discountAmount,
            DiscountNote = discountNote,
            Notes = notes,
            DebtDueDate = debtDueDate
        };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/students", req, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode)
        {
            var msg = await TryExtractErrorMessageAsync(res, ct);
            throw new InvalidOperationException(msg ?? $"Thêm học viên thất bại (Mã lỗi {(int)res.StatusCode}: {res.ReasonPhrase})");
        }
        return true;
    }

    public async Task<bool> UpdateCscaClassAsync(
        Guid classId, string name, string batch, string schedule, decimal tuitionFee,
        DateTime? startDate, DateTime? endDate, string status, Guid? courseId = null, CancellationToken ct = default)
    {
        var req = new { Name = name, Batch = batch, Schedule = schedule, TuitionFee = tuitionFee, StartDate = startDate, EndDate = endDate, CourseId = courseId, Status = status };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteCscaClassAsync(Guid classId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaStudentAsync(
        Guid classId, Guid studentId, string studentName, string email, string phone,
        int? age, string hometown, decimal paidAmount, int paymentStatus, string notes, DateTime? debtDueDate = null,
        decimal discountAmount = 0, string? discountNote = null, CancellationToken ct = default)
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
            DiscountAmount = discountAmount,
            DiscountNote = discountNote,
            Notes = notes,
            DebtDueDate = debtDueDate
        };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/students/{studentId}/payment", req, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode)
        {
            var msg = await TryExtractErrorMessageAsync(res, ct);
            throw new InvalidOperationException(msg ?? $"Cập nhật học viên thất bại (Mã lỗi {(int)res.StatusCode}: {res.ReasonPhrase})");
        }
        return true;
    }

    public async Task<bool> RemoveCscaStudentAsync(Guid classId, Guid studentId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/students/{studentId}", ct), ct);
        if (!res.IsSuccessStatusCode)
        {
            var msg = await TryExtractErrorMessageAsync(res, ct);
            throw new InvalidOperationException(msg ?? $"Xóa học viên thất bại (Mã lỗi {(int)res.StatusCode}: {res.ReasonPhrase})");
        }
        return true;
    }

    public async Task<bool> RemoveCscaStaffAsync(Guid classId, Guid staffId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/staff/{staffId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AssignStaffAsync(Guid classId, Guid employeeId, string roleInClass, decimal compensationRate, string? notes = null, CancellationToken ct = default)
    {
        var req = new { EmployeeId = employeeId, RoleInClass = roleInClass, CompensationRate = compensationRate, Notes = notes };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/staff", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaStaffAsync(Guid classId, Guid staffId, string roleInClass, decimal compensationRate, string? notes = null, CancellationToken ct = default)
    {
        var req = new { RoleInClass = roleInClass, CompensationRate = compensationRate, Notes = notes };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/staff/{staffId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AddCscaScheduleAsync(Guid classId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, string? room, string? meetingUrl, string? notes, Guid? classroomId = null, CancellationToken ct = default)
    {
        var req = new { DayOfWeek = dayOfWeek, StartTime = startTime, EndTime = endTime, Room = room, MeetingUrl = meetingUrl, Notes = notes, ClassroomId = classroomId };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/schedules", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaScheduleAsync(Guid classId, Guid scheduleId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, string? room, string? meetingUrl, string? notes, Guid? classroomId = null, CancellationToken ct = default)
    {
        var req = new { DayOfWeek = dayOfWeek, StartTime = startTime, EndTime = endTime, Room = room, MeetingUrl = meetingUrl, Notes = notes, ClassroomId = classroomId };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/schedules/{scheduleId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaScheduleAsync(Guid classId, Guid scheduleId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/schedules/{scheduleId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<CscaClassroomItem>?> GetCscaClassroomsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classrooms?includeInactive={includeInactive}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaClassroomItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaClassroomAsync(string code, string name, int? capacity, string? location, bool isActive, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Capacity = capacity, Location = location, IsActive = isActive };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/csca/classrooms", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaClassroomAsync(Guid classroomId, string name, int? capacity, string? location, bool isActive, CancellationToken ct = default)
    {
        var req = new { Name = name, Capacity = capacity, Location = location, IsActive = isActive };
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classrooms/{classroomId}", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaClassroomAsync(Guid classroomId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classrooms/{classroomId}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<CscaLessonSessionItem>?> GetCscaLessonSessionsAsync(Guid classId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}/sessions", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaLessonSessionItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaLessonSessionAsync(Guid classId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? classroomId, string? meetingUrl, string? notes, string status = "Scheduled", CancellationToken ct = default)
    {
        var req = new { LessonDate = lessonDate, StartTime = startTime, EndTime = endTime, ClassroomId = classroomId, MeetingUrl = meetingUrl, Notes = notes, Status = status };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/sessions", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCscaLessonSessionAsync(Guid classId, Guid sessionId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? classroomId, string? meetingUrl, string? notes, string status, CancellationToken ct = default)
    {
        var req = new { LessonDate = lessonDate, StartTime = startTime, EndTime = endTime, ClassroomId = classroomId, MeetingUrl = meetingUrl, Notes = notes, Status = status };
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveCscaLessonSessionAsync(Guid classId, Guid sessionId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<int?> GenerateCscaLessonSessionsAsync(Guid classId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var req = new { FromDate = fromDate, ToDate = toDate };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/generate", req, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<int>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<CscaLessonAttendanceItem>?> GetCscaLessonAttendanceAsync(Guid classId, Guid sessionId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}/attendance", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaLessonAttendanceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<CscaClassAttendanceReportItem?> GetCscaAttendanceReportAsync(
        Guid classId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
    {
        var url = $"api/v1/csca/classes/{classId}/attendance-report";
        var parameters = new List<string>();
        if (fromDate.HasValue) parameters.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
        if (toDate.HasValue) parameters.Add($"toDate={toDate.Value:yyyy-MM-dd}");
        if (parameters.Count > 0) url += "?" + string.Join("&", parameters);
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CscaClassAttendanceReportItem>>(_jsonOptions, ct);
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
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}/attendance", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

}
