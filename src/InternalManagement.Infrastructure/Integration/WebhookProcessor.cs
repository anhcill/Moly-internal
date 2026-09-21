using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Receives signed external events. CSCA LMS attendance is projected immediately
/// into the Management attendance ledger; other webhook types remain inbox-based.
/// </summary>
public sealed class WebhookProcessor : IWebhookProcessor
{
    private const string CscaLmsAttendanceEvent = "lms.attendance.recorded";
    private const string LmsActor = "CSCA_COURSE_LMS";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IApplicationDbContext _db;
    private readonly ILogger<WebhookProcessor> _logger;

    public WebhookProcessor(IApplicationDbContext db, ILogger<WebhookProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(bool Accepted, string Message)> IngestAsync(
        string sourceSystem, string eventId, string eventType,
        string payloadJson, string? signature, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem) || string.IsNullOrWhiteSpace(eventId) ||
            string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(payloadJson))
        {
            return (false, "Webhook thiếu sourceSystem, eventId, eventType hoặc payloadJson.");
        }

        if (!await ValidateSignatureAsync(sourceSystem, payloadJson, signature, ct))
        {
            _logger.LogWarning("Webhook HMAC không hợp lệ: {SourceSystem}/{EventId}", sourceSystem, eventId);
            return (false, "Chữ ký HMAC không hợp lệ.");
        }

        var existing = await _db.IntegrationInboxes
            .FirstOrDefaultAsync(i => i.SourceSystem == sourceSystem && i.EventId == eventId, ct);
        if (existing != null)
        {
            if (!string.Equals(existing.PayloadJson, payloadJson, StringComparison.Ordinal) ||
                !string.Equals(existing.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "eventId đã tồn tại với nội dung khác.");
            }

            if (!IsCscaLmsAttendance(sourceSystem, eventType) || existing.Status != IntegrationStatus.Failed)
            {
                _logger.LogInformation("Webhook duplicate bị bỏ qua: {SourceSystem}/{EventId}", sourceSystem, eventId);
                return (true, "Event đã được nhận trước đó (idempotent skip).");
            }

            return await ProjectCscaAttendanceAsync(existing, ct);
        }

        var inboxEntry = new IntegrationInbox
        {
            SourceSystem = sourceSystem,
            EventId = eventId,
            EventType = eventType,
            PayloadJson = payloadJson,
            Signature = signature,
            Status = IntegrationStatus.Pending,
            ReceivedAt = DateTime.UtcNow
        };
        _db.IntegrationInboxes.Add(inboxEntry);

        if (IsCscaLmsAttendance(sourceSystem, eventType))
        {
            return await ProjectCscaAttendanceAsync(inboxEntry, ct);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Webhook đã nhận: {SourceSystem}/{EventId}/{EventType}", sourceSystem, eventId, eventType);
        return (true, "Webhook đã được tiếp nhận thành công.");
    }

    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct)
    {
        var pendingItems = await _db.IntegrationInboxes
            .Where(i => i.Status == IntegrationStatus.Pending)
            .OrderBy(i => i.ReceivedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pendingItems.Count == 0) return 0;

        var processed = 0;
        foreach (var item in pendingItems)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (IsCscaLmsAttendance(item.SourceSystem, item.EventType))
                {
                    var result = await ProjectCscaAttendanceAsync(item, ct);
                    if (!result.Accepted) continue;
                }
                else
                {
                    item.Status = IntegrationStatus.Success;
                    item.ProcessedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }
                processed++;
            }
            catch (Exception ex)
            {
                item.Status = IntegrationStatus.Failed;
                item.ErrorMessage = TruncateError(ex.Message);
                item.ProcessedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogError(ex, "Inbox processing failed: {SourceSystem}/{EventId}", item.SourceSystem, item.EventId);
            }
        }

        return processed;
    }

    private async Task<(bool Accepted, string Message)> ProjectCscaAttendanceAsync(IntegrationInbox inbox, CancellationToken ct)
    {
        try
        {
            var payload = ParseAttendancePayload(inbox.PayloadJson);
            await UpsertCscaAttendanceAsync(payload, ct);
            inbox.Status = IntegrationStatus.Success;
            inbox.ErrorMessage = null;
            inbox.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("CSCA LMS attendance saved: {EventId}", inbox.EventId);
            return (true, "Điểm danh đã được lưu trực tiếp vào hệ thống quản lý.");
        }
        catch (CscaLmsDependencyException ex)
        {
            inbox.Status = IntegrationStatus.Failed;
            inbox.ErrorMessage = TruncateError(ex.Message);
            inbox.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("CSCA LMS attendance is waiting for mapping: {EventId}. {Message}", inbox.EventId, ex.Message);
            return (false, $"DEPENDENCY_PENDING: {ex.Message}");
        }
        catch (CscaLmsValidationException ex)
        {
            inbox.Status = IntegrationStatus.Failed;
            inbox.ErrorMessage = TruncateError(ex.Message);
            inbox.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("CSCA LMS attendance payload rejected: {EventId}. {Message}", inbox.EventId, ex.Message);
            return (false, ex.Message);
        }
    }

    private async Task UpsertCscaAttendanceAsync(CscaLmsAttendancePayload payload, CancellationToken ct)
    {
        if (!Guid.TryParse(payload.ManagementClassId, out var classId))
            throw new CscaLmsValidationException("managementClassId không hợp lệ.");
        if (payload.SchemaVersion != 1)
            throw new CscaLmsValidationException("schemaVersion điểm danh LMS chưa được hỗ trợ.");
        if (payload.LmsSession == null || string.IsNullOrWhiteSpace(payload.LmsSession.Id) || payload.LmsSession.Id.Length > 128)
            throw new CscaLmsValidationException("lmsSession.id không hợp lệ.");
        if (payload.LmsSession.StartTime is not { } startTime || payload.LmsSession.EndTime is not { } endTime || endTime <= startTime)
            throw new CscaLmsValidationException("Thời gian buổi học không hợp lệ.");
        if (payload.Attendance is not { Count: > 0 and <= 1000 })
            throw new CscaLmsValidationException("Danh sách điểm danh phải có từ 1 đến 1000 học viên.");
        if (!await _db.CscaClasses.AnyAsync(cls => cls.Id == classId && !cls.IsDeleted, ct))
            throw new CscaLmsDependencyException("Lớp Management chưa tồn tại hoặc đã bị xóa.");

        var normalizedAttendance = payload.Attendance.Select(NormalizeAttendance).ToList();
        if (normalizedAttendance.Select(item => item.StudentId).Distinct().Count() != normalizedAttendance.Count)
            throw new CscaLmsValidationException("Một học viên chỉ được xuất hiện một lần trong điểm danh.");

        var localStart = TimeZoneInfo.ConvertTime(startTime, VietnamTimeZone());
        var localEnd = TimeZoneInfo.ConvertTime(endTime, VietnamTimeZone());
        var source = LmsIntegrationSourceSystems.CscaCourseLms;
        var session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item =>
            item.ExternalSource == source && item.ExternalSessionId == payload.LmsSession.Id, ct);

        if (session != null && session.ClassId != classId)
            throw new CscaLmsValidationException("lmsSession.id đang được liên kết với một lớp khác.");

        if (session == null)
        {
            session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item =>
                item.ClassId == classId &&
                item.LessonDate == DateOnly.FromDateTime(localStart.DateTime) &&
                item.StartTime == localStart.TimeOfDay &&
                item.EndTime == localEnd.TimeOfDay, ct);
        }

        if (session == null)
        {
            session = new CscaLessonSession
            {
                ClassId = classId,
                LessonDate = DateOnly.FromDateTime(localStart.DateTime),
                StartTime = localStart.TimeOfDay,
                EndTime = localEnd.TimeOfDay,
                Status = MapSessionStatus(payload.LmsSession.Status),
                ExternalSource = source,
                ExternalSessionId = payload.LmsSession.Id.Trim(),
                Notes = BuildImportedSessionNote(payload.LmsSession.Title),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = LmsActor
            };
            _db.CscaLessonSessions.Add(session);
        }
        else
        {
            session.ExternalSource = source;
            session.ExternalSessionId = payload.LmsSession.Id.Trim();
            session.Status = MapSessionStatus(payload.LmsSession.Status);
            session.UpdatedAt = DateTime.UtcNow;
            session.UpdatedBy = LmsActor;
        }

        var studentIds = normalizedAttendance.Select(item => item.StudentId).ToList();
        var students = await _db.CscaClassStudents
            .Where(student => student.ClassId == classId && studentIds.Contains(student.Id))
            .Select(student => student.Id)
            .ToListAsync(ct);
        if (students.Count != studentIds.Count)
            throw new CscaLmsDependencyException("Có học viên LMS chưa được liên kết với đúng lớp Management.");

        var existing = await _db.CscaLessonAttendances
            .Where(item => item.LessonSessionId == session.Id && studentIds.Contains(item.StudentId))
            .ToDictionaryAsync(item => item.StudentId, ct);
        foreach (var item in normalizedAttendance)
        {
            if (!existing.TryGetValue(item.StudentId, out var attendance))
            {
                attendance = new CscaLessonAttendance
                {
                    LessonSession = session,
                    StudentId = item.StudentId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = LmsActor
                };
                _db.CscaLessonAttendances.Add(attendance);
            }
            attendance.Status = item.Status;
            attendance.CheckInAt = item.CheckedAt?.UtcDateTime ?? DateTime.UtcNow;
            attendance.Notes = item.Note;
            attendance.UpdatedAt = DateTime.UtcNow;
            attendance.UpdatedBy = LmsActor;
        }
    }

    private async Task<bool> ValidateSignatureAsync(string sourceSystem, string payloadJson, string? signature, CancellationToken ct)
    {
        var source = await _db.IntegrationSources
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Code == sourceSystem && item.IsActive, ct);

        if (source == null)
        {
            // Existing unsigned generic webhooks remain supported for local development.
            return string.IsNullOrWhiteSpace(signature) && !string.Equals(sourceSystem, LmsIntegrationSourceSystems.CscaCourseLms, StringComparison.Ordinal);
        }

        var secret = source.CredentialReference;
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("IntegrationSource {SourceSystem} chưa cấu hình CredentialReference — HMAC bị bỏ qua.", sourceSystem);
            return true;
        }
        if (string.IsNullOrWhiteSpace(signature)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        var computedSignature = $"sha256={Convert.ToHexStringLower(computedHash)}";
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature));
    }

    private static bool IsCscaLmsAttendance(string sourceSystem, string eventType) =>
        string.Equals(sourceSystem, LmsIntegrationSourceSystems.CscaCourseLms, StringComparison.Ordinal) &&
        string.Equals(eventType, CscaLmsAttendanceEvent, StringComparison.OrdinalIgnoreCase);

    private static CscaLmsAttendancePayload ParseAttendancePayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<CscaLmsAttendancePayload>(payloadJson, JsonOptions)
                   ?? throw new CscaLmsValidationException("Payload điểm danh LMS không hợp lệ.");
        }
        catch (JsonException)
        {
            throw new CscaLmsValidationException("Payload điểm danh LMS không phải JSON hợp lệ.");
        }
    }

    private static CscaLmsAttendanceItemNormalized NormalizeAttendance(CscaLmsAttendanceItem item)
    {
        if (!Guid.TryParse(item.ManagementStudentId, out var studentId))
            throw new CscaLmsValidationException("managementStudentId không hợp lệ.");
        var note = item.Note?.Trim();
        if (note?.Length > 1000) throw new CscaLmsValidationException("Ghi chú điểm danh quá dài.");
        var status = item.Status?.Trim().ToLowerInvariant() switch
        {
            "present" => "Present",
            "late" => "Late",
            "absent" => "Absent",
            "excused" => "Excused",
            _ => throw new CscaLmsValidationException("Trạng thái điểm danh không hợp lệ.")
        };
        return new CscaLmsAttendanceItemNormalized(studentId, status, item.CheckedAt, note);
    }

    private static string MapSessionStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "cancelled" or "canceled" => "Cancelled",
        "completed" => "Completed",
        "rescheduled" => "Rescheduled",
        _ => "Scheduled"
    };

    private static string BuildImportedSessionNote(string? title)
    {
        var suffix = string.IsNullOrWhiteSpace(title) ? string.Empty : $": {title.Trim()}";
        var note = $"Imported from CSCA Course LMS{suffix}";
        return note.Length <= 1000 ? note : "Imported from CSCA Course LMS";
    }

    private static TimeZoneInfo VietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static string TruncateError(string value) => value.Length <= 2000 ? value : value[..2000];

    private sealed record CscaLmsAttendancePayload(
        int SchemaVersion,
        string? ManagementClassId,
        CscaLmsSessionPayload? LmsSession,
        List<CscaLmsAttendanceItem>? Attendance);

    private sealed record CscaLmsSessionPayload(
        string? Id,
        string? Title,
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime,
        string? Status);

    private sealed record CscaLmsAttendanceItem(
        string? ManagementStudentId,
        string? Status,
        DateTimeOffset? CheckedAt,
        string? Note);

    private sealed record CscaLmsAttendanceItemNormalized(
        Guid StudentId,
        string Status,
        DateTimeOffset? CheckedAt,
        string? Note);

    private sealed class CscaLmsValidationException(string message) : Exception(message);
    private sealed class CscaLmsDependencyException(string message) : Exception(message);
}
