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
        await EnsureCscaSuccessAsync(response, "Tải danh sách lớp CSCA thất bại", ct);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CscaClassItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaStudentDirectoryItem>?> GetCscaStudentDirectoryAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/students";
        if (!string.IsNullOrWhiteSpace(search)) url += $"?search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải danh sách học viên CSCA thất bại", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaStudentDirectoryItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlineMaterialItem>?> GetCscaOnlineMaterialsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/materials?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải tài liệu CSCA từ web thất bại", ct);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlineMaterialItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlineVocabularyItem>?> GetCscaOnlineVocabularyAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/vocabulary?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải từ vựng CSCA từ web thất bại", ct);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlineVocabularyItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<CscaOnlinePostItem>?> GetCscaOnlinePostsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/csca/online/posts?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải bài cộng đồng CSCA từ web thất bại", ct);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaOnlinePostItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<CscaClassDetailItem?> GetCscaClassDetailAsync(Guid classId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}", ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải chi tiết lớp CSCA thất bại", ct);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CscaClassDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaClassAsync(string code, string name, string batch, string schedule, decimal tuitionFee, Guid? courseId = null, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Batch = batch, Schedule = schedule, TuitionFee = tuitionFee, CourseId = courseId, Status = "Active" };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/csca/classes", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Tạo lớp CSCA thất bại", ct);
    }

    public async Task<bool> EnrollStudentAsync(Guid classId, string studentName, string email, string phone, decimal paidAmount, CancellationToken ct = default)
    {
        var req = new { StudentName = studentName, Email = email, PhoneNumber = phone, PaidAmount = paidAmount, PaymentStatus = paidAmount > 0 ? 1 : 0 };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/students", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Ghi danh học viên thất bại", ct);
    }

    private async Task<bool> EnsureCscaSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return true;
        var detail = await TryExtractErrorMessageAsync(response, ct);
        var nextStep = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Đăng nhập lại rồi thử thao tác.",
            HttpStatusCode.Forbidden => "Tài khoản cần được cấp quyền cho thao tác này.",
            HttpStatusCode.Conflict => "Tải lại dữ liệu rồi kiểm tra bản ghi đã thay đổi.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => "Kiểm tra kết nối API và dịch vụ liên quan rồi thử lại.",
            _ => null
        };
        var suffix = string.IsNullOrWhiteSpace(detail)
            ? $"API trả HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            : $"{detail} (HTTP {(int)response.StatusCode})";
        throw new InvalidOperationException($"{action}: {suffix}{(nextStep is null ? string.Empty : $" {nextStep}")}");
    }

    private async Task<string?> TryExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return body.Length <= 500 ? body : body[..500] + "…";
            var message = root.TryGetProperty("message", out var msgElem) && msgElem.ValueKind == JsonValueKind.String
                ? msgElem.GetString()
                : root.TryGetProperty("Message", out var msgElemCap) && msgElemCap.ValueKind == JsonValueKind.String
                    ? msgElemCap.GetString() : null;
            if (root.TryGetProperty("errors", out var errElem) || root.TryGetProperty("Errors", out errElem))
            {
                if (errElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var list = errElem.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (list.Count > 0) return string.Join("\n", list);
                }
                else if (errElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var list = errElem.EnumerateObject()
                        .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                        .SelectMany(p => p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                            .Select(v => $"{p.Name}: {v.GetString()}"))
                        .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (list.Count > 0) return string.Join("\n", list);
                }
            }
            if (root.TryGetProperty("detail", out var detailElem) && detailElem.ValueKind == JsonValueKind.String)
            {
                var detail = detailElem.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    var suggestion = root.TryGetProperty("suggestion", out var suggestionElem) &&
                        suggestionElem.ValueKind == JsonValueKind.String ? suggestionElem.GetString() : null;
                    var correlationId = root.TryGetProperty("correlationId", out var correlationElem) &&
                        correlationElem.ValueKind == JsonValueKind.String ? correlationElem.GetString() : null;
                    return detail +
                        (string.IsNullOrWhiteSpace(suggestion) ? string.Empty : $" Cách xử lý: {suggestion}") +
                        (string.IsNullOrWhiteSpace(correlationId) ? string.Empty : $" Mã yêu cầu: {correlationId}");
                }
            }
            if (!string.IsNullOrWhiteSpace(message)) return message;
            if (root.TryGetProperty("title", out var titleElem) && titleElem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return titleElem.GetString();
            }
        }
        catch (JsonException)
        {
            // Proxy and gateway failures may return plain text instead of the API envelope.
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body) && !body.TrimStart().StartsWith('<'))
                return body.Length <= 500 ? body.Trim() : body[..500].Trim() + "…";
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
        return await EnsureCscaSuccessAsync(res, "Thêm học viên vào lớp thất bại", ct);
    }

    public async Task<bool> UpdateCscaClassAsync(
        Guid classId, string name, string batch, string schedule, decimal tuitionFee,
        DateTime? startDate, DateTime? endDate, string status, Guid? courseId = null, CancellationToken ct = default)
    {
        var req = new { Name = name, Batch = batch, Schedule = schedule, TuitionFee = tuitionFee, StartDate = startDate, EndDate = endDate, CourseId = courseId, Status = status };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Cập nhật lớp CSCA thất bại", ct);
    }

    public async Task<bool> DeleteCscaClassAsync(Guid classId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}", ct), ct);
        return await EnsureCscaSuccessAsync(res, "Xóa lớp CSCA thất bại", ct);
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
        return await EnsureCscaSuccessAsync(res, "Cập nhật học viên và học phí thất bại", ct);
    }

    public async Task<bool> RemoveCscaStudentAsync(Guid classId, Guid studentId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/students/{studentId}", ct), ct);
        return await EnsureCscaSuccessAsync(res, "Xóa học viên khỏi lớp thất bại", ct);
    }

    public async Task<bool> RemoveCscaStaffAsync(Guid classId, Guid staffId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/staff/{staffId}", ct), ct);
        return await EnsureCscaSuccessAsync(res, "Hủy phân công nhân sự thất bại", ct);
    }

    public async Task<bool> AssignStaffAsync(Guid classId, Guid employeeId, string roleInClass, decimal compensationRate, string? notes = null, CancellationToken ct = default)
    {
        var req = new { EmployeeId = employeeId, RoleInClass = roleInClass, CompensationRate = compensationRate, Notes = notes };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/staff", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Phân công nhân sự thất bại", ct);
    }

    public async Task<bool> UpdateCscaStaffAsync(Guid classId, Guid staffId, string roleInClass, decimal compensationRate, string? notes = null, CancellationToken ct = default)
    {
        var req = new { RoleInClass = roleInClass, CompensationRate = compensationRate, Notes = notes };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/staff/{staffId}", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Cập nhật phân công thất bại", ct);
    }

    public async Task<bool> AddCscaScheduleAsync(Guid classId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, string? room, string? meetingUrl, string? notes, Guid? classroomId = null, CancellationToken ct = default)
    {
        var req = new { DayOfWeek = dayOfWeek, StartTime = startTime, EndTime = endTime, Room = room, MeetingUrl = meetingUrl, Notes = notes, ClassroomId = classroomId };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/schedules", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Tạo khung giờ cố định thất bại", ct);
    }

    public async Task<bool> UpdateCscaScheduleAsync(Guid classId, Guid scheduleId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, string? room, string? meetingUrl, string? notes, Guid? classroomId = null, CancellationToken ct = default)
    {
        var req = new { DayOfWeek = dayOfWeek, StartTime = startTime, EndTime = endTime, Room = room, MeetingUrl = meetingUrl, Notes = notes, ClassroomId = classroomId };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/schedules/{scheduleId}", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(res, "Cập nhật khung giờ cố định thất bại", ct);
    }

    public async Task<bool> RemoveCscaScheduleAsync(Guid classId, Guid scheduleId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/schedules/{scheduleId}", ct), ct);
        return await EnsureCscaSuccessAsync(res, "Xóa khung giờ cố định thất bại", ct);
    }

    public async Task<IReadOnlyList<CscaClassroomItem>?> GetCscaClassroomsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classrooms?includeInactive={includeInactive}", ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải danh sách phòng học thất bại", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaClassroomItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaClassroomAsync(string code, string name, int? capacity, string? location, bool isActive, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Capacity = capacity, Location = location, IsActive = isActive };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/csca/classrooms", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(response, "Tạo phòng học thất bại", ct);
    }

    public async Task<bool> UpdateCscaClassroomAsync(Guid classroomId, string name, int? capacity, string? location, bool isActive, CancellationToken ct = default)
    {
        var req = new { Name = name, Capacity = capacity, Location = location, IsActive = isActive };
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classrooms/{classroomId}", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(response, "Cập nhật phòng học thất bại", ct);
    }

    public async Task<bool> RemoveCscaClassroomAsync(Guid classroomId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classrooms/{classroomId}", ct), ct);
        return await EnsureCscaSuccessAsync(response, "Xóa phòng học thất bại", ct);
    }

    public async Task<IReadOnlyList<CscaLessonSessionItem>?> GetCscaLessonSessionsAsync(Guid classId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}/sessions", ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải các buổi học thất bại", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CscaLessonSessionItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCscaLessonSessionAsync(Guid classId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? classroomId, string? meetingUrl, string? notes, string status = "Scheduled", CancellationToken ct = default)
    {
        var req = new { LessonDate = lessonDate, StartTime = startTime, EndTime = endTime, ClassroomId = classroomId, MeetingUrl = meetingUrl, Notes = notes, Status = status };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/sessions", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(response, "Tạo buổi học thất bại", ct);
    }

    public async Task<bool> UpdateCscaLessonSessionAsync(Guid classId, Guid sessionId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? classroomId, string? meetingUrl, string? notes, string status, CancellationToken ct = default)
    {
        var req = new { LessonDate = lessonDate, StartTime = startTime, EndTime = endTime, ClassroomId = classroomId, MeetingUrl = meetingUrl, Notes = notes, Status = status };
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}", req, _jsonOptions, ct), ct);
        return await EnsureCscaSuccessAsync(response, "Cập nhật buổi học thất bại", ct);
    }

    public async Task<bool> RemoveCscaLessonSessionAsync(Guid classId, Guid sessionId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}", ct), ct);
        return await EnsureCscaSuccessAsync(response, "Xóa buổi học thất bại", ct);
    }

    public async Task<int?> GenerateCscaLessonSessionsAsync(Guid classId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var req = new { FromDate = fromDate, ToDate = toDate };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/csca/classes/{classId}/sessions/generate", req, _jsonOptions, ct), ct);
        await EnsureCscaSuccessAsync(response, "Tạo buổi học từ lịch cố định thất bại", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<int>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<CscaLessonAttendanceItem>?> GetCscaLessonAttendanceAsync(Guid classId, Guid sessionId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/csca/classes/{classId}/sessions/{sessionId}/attendance", ct), ct);
        await EnsureCscaSuccessAsync(response, "Tải điểm danh buổi học thất bại", ct);
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
        await EnsureCscaSuccessAsync(response, "Tải báo cáo điểm danh từ web thất bại", ct);
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
        return await EnsureCscaSuccessAsync(response, "Lưu điểm danh thất bại", ct);
    }

}
