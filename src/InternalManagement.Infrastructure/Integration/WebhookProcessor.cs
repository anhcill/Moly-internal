using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Receives signed external events. CSCA LMS attendance and calendar data are
/// projected immediately into Management read models; other webhook types remain
/// inbox-based.
/// </summary>
public sealed class WebhookProcessor : IWebhookProcessor
{
    private const string CscaLmsAttendanceEvent = "lms.attendance.recorded";
    private const string CscaLmsScheduleUpsertedEvent = "lms.schedule.upserted";
    private const string CscaLmsScheduleArchivedEvent = "lms.schedule.archived";
    private const string CscaLmsSessionUpsertedEvent = "lms.session.upserted";
    private const string CscaLmsSessionCancelledEvent = "lms.session.cancelled";
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

            if (!IsCscaLmsProjection(sourceSystem, eventType) || existing.Status != IntegrationStatus.Failed)
            {
                _logger.LogInformation("Webhook duplicate bị bỏ qua: {SourceSystem}/{EventId}", sourceSystem, eventId);
                return (true, "Event đã được nhận trước đó (idempotent skip).");
            }

            return await ProjectCscaLmsProjectionAsync(existing, ct);
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

        if (IsCscaLmsProjection(sourceSystem, eventType))
        {
            return await ProjectCscaLmsProjectionAsync(inboxEntry, ct);
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
                if (IsCscaLmsProjection(item.SourceSystem, item.EventType))
                {
                    var result = await ProjectCscaLmsProjectionAsync(item, ct);
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

    private Task<(bool Accepted, string Message)> ProjectCscaLmsProjectionAsync(IntegrationInbox inbox, CancellationToken ct) =>
        IsCscaLmsAttendance(inbox.SourceSystem, inbox.EventType)
            ? ProjectCscaAttendanceAsync(inbox, ct)
            : ProjectCscaCalendarAsync(inbox, ct);

    private async Task<(bool Accepted, string Message)> ProjectCscaCalendarAsync(IntegrationInbox inbox, CancellationToken ct)
    {
        try
        {
            var payload = ParseCalendarPayload(inbox.PayloadJson);
            if (IsCscaLmsScheduleUpserted(inbox.SourceSystem, inbox.EventType))
                await UpsertCscaScheduleAsync(payload, ct);
            else if (IsCscaLmsScheduleArchived(inbox.SourceSystem, inbox.EventType))
                await ArchiveCscaScheduleAsync(payload, ct);
            else if (IsCscaLmsSessionEvent(inbox.SourceSystem, inbox.EventType))
                await UpsertCscaSessionAsync(payload, inbox.EventType, ct);
            else
                throw new CscaLmsValidationException("Loại sự kiện lịch LMS không được hỗ trợ.");

            inbox.Status = IntegrationStatus.Success;
            inbox.ErrorMessage = null;
            inbox.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("CSCA LMS calendar projection saved: {EventId}/{EventType}", inbox.EventId, inbox.EventType);
            return (true, "Lịch LMS đã được lưu vào bản chiếu quản lý.");
        }
        catch (CscaLmsDependencyException ex)
        {
            inbox.Status = IntegrationStatus.Failed;
            inbox.ErrorMessage = TruncateError(ex.Message);
            inbox.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("CSCA LMS calendar is waiting for mapping: {EventId}. {Message}", inbox.EventId, ex.Message);
            return (false, $"DEPENDENCY_PENDING: {ex.Message}");
        }
        catch (CscaLmsValidationException ex)
        {
            inbox.Status = IntegrationStatus.Failed;
            inbox.ErrorMessage = TruncateError(ex.Message);
            inbox.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("CSCA LMS calendar payload rejected: {EventId}. {Message}", inbox.EventId, ex.Message);
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

    private async Task UpsertCscaScheduleAsync(CscaLmsCalendarPayload payload, CancellationToken ct)
    {
        var classId = ValidateManagementClass(payload.ManagementClassId);
        if (payload.SchemaVersion != 1)
            throw new CscaLmsValidationException("schemaVersion lịch LMS chưa được hỗ trợ.");
        var incoming = payload.LmsSchedule ?? throw new CscaLmsValidationException("lmsSchedule không được để trống.");
        ValidateSchedule(incoming);
        await EnsureManagementClassExistsAsync(classId, ct);

        var source = LmsIntegrationSourceSystems.CscaCourseLms;
        var schedule = await _db.CscaClassSchedules.FirstOrDefaultAsync(item =>
            item.ExternalSource == source && item.ExternalScheduleId == incoming.Id, ct);
        if (schedule is not null && schedule.ClassId != classId)
            throw new CscaLmsValidationException("lmsSchedule.id đang được liên kết với một lớp khác.");
        if (schedule is not null && incoming.Version <= schedule.ExternalVersion)
            return;

        var dayOfWeek = ToManagementDayOfWeek(incoming.DayOfWeek);
        var startTime = ParseClockTime(incoming.StartTime, "lmsSchedule.startTime");
        var endTime = ParseClockTime(incoming.EndTime, "lmsSchedule.endTime");
        var startDate = ParseDateOnly(incoming.StartDate, "lmsSchedule.startDate");
        var endDate = ParseDateOnly(incoming.EndDate, "lmsSchedule.endDate");

        if (schedule is null)
        {
            // Existing pre-LMS schedules may be adopted only when the slot is
            // unlinked. This preserves reporting history without guessing a
            // mapping from a title.
            schedule = await _db.CscaClassSchedules.FirstOrDefaultAsync(item =>
                item.ClassId == classId && item.DayOfWeek == dayOfWeek &&
                item.StartTime == startTime && item.EndTime == endTime, ct);
            if (schedule is not null && (!string.IsNullOrWhiteSpace(schedule.ExternalSource) ||
                                         !string.IsNullOrWhiteSpace(schedule.ExternalScheduleId)))
            {
                throw new CscaLmsValidationException("Khung lịch Management đã liên kết với nguồn khác.");
            }
        }

        if (schedule is null)
        {
            schedule = new CscaClassSchedule
            {
                ClassId = classId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = LmsActor
            };
            _db.CscaClassSchedules.Add(schedule);
        }

        schedule.DayOfWeek = dayOfWeek;
        schedule.StartTime = startTime;
        schedule.EndTime = endTime;
        schedule.Title = TrimRequired(incoming.Title, "lmsSchedule.title", 255);
        schedule.Timezone = TrimRequired(incoming.Timezone, "lmsSchedule.timezone", 64);
        schedule.StartDate = startDate;
        schedule.EndDate = endDate;
        schedule.Status = MapScheduleStatus(incoming.Status);
        schedule.ExternalSource = source;
        schedule.ExternalScheduleId = incoming.Id!.Trim();
        schedule.ExternalVersion = incoming.Version;
        schedule.UpdatedAt = DateTime.UtcNow;
        schedule.UpdatedBy = LmsActor;
    }

    private async Task ArchiveCscaScheduleAsync(CscaLmsCalendarPayload payload, CancellationToken ct)
    {
        var classId = ValidateManagementClass(payload.ManagementClassId);
        if (payload.SchemaVersion != 1)
            throw new CscaLmsValidationException("schemaVersion lịch LMS chưa được hỗ trợ.");
        var incoming = payload.LmsSchedule ?? throw new CscaLmsValidationException("lmsSchedule không được để trống.");
        if (string.IsNullOrWhiteSpace(incoming.Id) || incoming.Id.Length > 128 || incoming.Version < 1)
            throw new CscaLmsValidationException("lmsSchedule archive không hợp lệ.");
        await EnsureManagementClassExistsAsync(classId, ct);

        var schedule = await _db.CscaClassSchedules.FirstOrDefaultAsync(item =>
            item.ExternalSource == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.ExternalScheduleId == incoming.Id, ct);
        if (schedule is null)
            throw new CscaLmsDependencyException("Lịch LMS chưa tồn tại ở Management; sẽ thử lại theo thứ tự outbox.");
        if (schedule.ClassId != classId)
            throw new CscaLmsValidationException("lmsSchedule.id đang được liên kết với một lớp khác.");
        if (incoming.Version <= schedule.ExternalVersion) return;

        schedule.Status = "Archived";
        schedule.ExternalVersion = incoming.Version;
        schedule.UpdatedAt = DateTime.UtcNow;
        schedule.UpdatedBy = LmsActor;
    }

    private async Task UpsertCscaSessionAsync(CscaLmsCalendarPayload payload, string eventType, CancellationToken ct)
    {
        var classId = ValidateManagementClass(payload.ManagementClassId);
        if (payload.SchemaVersion != 1)
            throw new CscaLmsValidationException("schemaVersion lịch LMS chưa được hỗ trợ.");
        var incoming = payload.LmsSession ?? throw new CscaLmsValidationException("lmsSession không được để trống.");
        ValidateSession(incoming);
        await EnsureManagementClassExistsAsync(classId, ct);

        var localStart = TimeZoneInfo.ConvertTime(incoming.StartTime!.Value, VietnamTimeZone());
        var localEnd = TimeZoneInfo.ConvertTime(incoming.EndTime!.Value, VietnamTimeZone());
        var source = LmsIntegrationSourceSystems.CscaCourseLms;
        var session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item =>
            item.ExternalSource == source && item.ExternalSessionId == incoming.Id, ct);
        if (session is not null && session.ClassId != classId)
            throw new CscaLmsValidationException("lmsSession.id đang được liên kết với một lớp khác.");
        if (session is not null && incoming.Version <= session.ExternalVersion) return;

        if (session is null)
        {
            session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item =>
                item.ClassId == classId &&
                item.LessonDate == DateOnly.FromDateTime(localStart.DateTime) &&
                item.StartTime == localStart.TimeOfDay && item.EndTime == localEnd.TimeOfDay, ct);
            if (session is not null && (!string.IsNullOrWhiteSpace(session.ExternalSource) ||
                                        !string.IsNullOrWhiteSpace(session.ExternalSessionId)))
                throw new CscaLmsValidationException("Buổi học Management đã liên kết với nguồn khác.");
        }

        if (session is null)
        {
            session = new CscaLessonSession
            {
                ClassId = classId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = LmsActor
            };
            _db.CscaLessonSessions.Add(session);
        }

        CscaClassSchedule? schedule = null;
        if (!string.IsNullOrWhiteSpace(incoming.ScheduleId))
        {
            schedule = await _db.CscaClassSchedules.FirstOrDefaultAsync(item =>
                item.ExternalSource == source && item.ExternalScheduleId == incoming.ScheduleId, ct);
            if (schedule is null)
                throw new CscaLmsDependencyException("Lịch nguồn của buổi học chưa có ở Management; sẽ thử lại.");
            if (schedule.ClassId != classId)
                throw new CscaLmsValidationException("lmsSession.scheduleId không thuộc lớp Management này.");
        }

        session.ScheduleId = schedule?.Id;
        session.LessonDate = DateOnly.FromDateTime(localStart.DateTime);
        session.StartTime = localStart.TimeOfDay;
        session.EndTime = localEnd.TimeOfDay;
        session.Status = string.Equals(eventType, CscaLmsSessionCancelledEvent, StringComparison.OrdinalIgnoreCase)
            ? "Cancelled"
            : MapSessionStatus(incoming.Status);
        session.MeetingUrl = TrimOptional(incoming.MeetingUrl, "lmsSession.meetingUrl", 1000);
        session.Notes = BuildImportedSessionNote(incoming.Title);
        session.ExternalSource = source;
        session.ExternalSessionId = incoming.Id!.Trim();
        session.ExternalVersion = incoming.Version;
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedBy = LmsActor;
    }

    private async Task EnsureManagementClassExistsAsync(Guid classId, CancellationToken ct)
    {
        if (!await _db.CscaClasses.AnyAsync(cls => cls.Id == classId && !cls.IsDeleted, ct))
            throw new CscaLmsDependencyException("Lớp Management chưa tồn tại hoặc đã bị xóa.");
    }

    private static Guid ValidateManagementClass(string? managementClassId)
    {
        if (!Guid.TryParse(managementClassId, out var classId))
            throw new CscaLmsValidationException("managementClassId không hợp lệ.");
        return classId;
    }

    private static void ValidateSchedule(CscaLmsSchedulePayload incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Id) || incoming.Id.Length > 128 || incoming.Version < 1)
            throw new CscaLmsValidationException("lmsSchedule.id hoặc version không hợp lệ.");
        if (incoming.DayOfWeek is < 1 or > 7)
            throw new CscaLmsValidationException("lmsSchedule.dayOfWeek phải theo ISO 1..7.");
        var start = ParseClockTime(incoming.StartTime, "lmsSchedule.startTime");
        var end = ParseClockTime(incoming.EndTime, "lmsSchedule.endTime");
        if (end <= start) throw new CscaLmsValidationException("Khung giờ lmsSchedule không hợp lệ.");
        if (ParseDateOnly(incoming.EndDate, "lmsSchedule.endDate") < ParseDateOnly(incoming.StartDate, "lmsSchedule.startDate"))
            throw new CscaLmsValidationException("Khoảng ngày lmsSchedule không hợp lệ.");
        _ = TrimRequired(incoming.Title, "lmsSchedule.title", 255);
        _ = TrimRequired(incoming.Timezone, "lmsSchedule.timezone", 64);
        _ = MapScheduleStatus(incoming.Status);
    }

    private static void ValidateSession(CscaLmsSessionPayload incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Id) || incoming.Id.Length > 128 || incoming.Version < 1)
            throw new CscaLmsValidationException("lmsSession.id hoặc version không hợp lệ.");
        if (incoming.StartTime is not { } start || incoming.EndTime is not { } end || end <= start)
            throw new CscaLmsValidationException("Thời gian buổi học không hợp lệ.");
        if (incoming.ScheduleId?.Length > 128)
            throw new CscaLmsValidationException("lmsSession.scheduleId không hợp lệ.");
        _ = TrimOptional(incoming.MeetingUrl, "lmsSession.meetingUrl", 1000);
    }

    private static int ToManagementDayOfWeek(int isoDayOfWeek) => isoDayOfWeek == 7 ? 0 : isoDayOfWeek;

    private static TimeSpan ParseClockTime(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            throw new CscaLmsValidationException($"{field} không hợp lệ.");
        return parsed;
    }

    private static DateOnly ParseDateOnly(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            throw new CscaLmsValidationException($"{field} không hợp lệ.");
        return parsed;
    }

    private static string TrimRequired(string? value, string field, int maxLength)
    {
        var result = TrimOptional(value, field, maxLength);
        return result ?? throw new CscaLmsValidationException($"{field} không được để trống.");
    }

    private static string? TrimOptional(string? value, string field, int maxLength)
    {
        if (value is null) return null;
        var result = value.Trim();
        if (result.Length > maxLength)
            throw new CscaLmsValidationException($"{field} vượt quá {maxLength} ký tự.");
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string MapScheduleStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "active" => "Active",
        "archived" => "Archived",
        _ => throw new CscaLmsValidationException("Trạng thái lmsSchedule không hợp lệ.")
    };

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

    private static bool IsCscaLmsScheduleUpserted(string sourceSystem, string eventType) =>
        string.Equals(sourceSystem, LmsIntegrationSourceSystems.CscaCourseLms, StringComparison.Ordinal) &&
        string.Equals(eventType, CscaLmsScheduleUpsertedEvent, StringComparison.OrdinalIgnoreCase);

    private static bool IsCscaLmsScheduleArchived(string sourceSystem, string eventType) =>
        string.Equals(sourceSystem, LmsIntegrationSourceSystems.CscaCourseLms, StringComparison.Ordinal) &&
        string.Equals(eventType, CscaLmsScheduleArchivedEvent, StringComparison.OrdinalIgnoreCase);

    private static bool IsCscaLmsSessionEvent(string sourceSystem, string eventType) =>
        string.Equals(sourceSystem, LmsIntegrationSourceSystems.CscaCourseLms, StringComparison.Ordinal) &&
        (string.Equals(eventType, CscaLmsSessionUpsertedEvent, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(eventType, CscaLmsSessionCancelledEvent, StringComparison.OrdinalIgnoreCase));

    private static bool IsCscaLmsProjection(string sourceSystem, string eventType) =>
        IsCscaLmsAttendance(sourceSystem, eventType) ||
        IsCscaLmsScheduleUpserted(sourceSystem, eventType) ||
        IsCscaLmsScheduleArchived(sourceSystem, eventType) ||
        IsCscaLmsSessionEvent(sourceSystem, eventType);

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

    private static CscaLmsCalendarPayload ParseCalendarPayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<CscaLmsCalendarPayload>(payloadJson, JsonOptions)
                   ?? throw new CscaLmsValidationException("Payload lịch LMS không hợp lệ.");
        }
        catch (JsonException)
        {
            throw new CscaLmsValidationException("Payload lịch LMS không phải JSON hợp lệ.");
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

    private sealed record CscaLmsCalendarPayload(
        int SchemaVersion,
        string? ManagementClassId,
        CscaLmsSchedulePayload? LmsSchedule,
        CscaLmsSessionPayload? LmsSession);

    private sealed record CscaLmsSchedulePayload(
        string? Id,
        string? LiveClassId,
        string? Title,
        int DayOfWeek,
        string? StartTime,
        string? EndTime,
        string? Timezone,
        string? StartDate,
        string? EndDate,
        string? Status,
        int Version);

    private sealed record CscaLmsSessionPayload(
        string? Id,
        string? Title,
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime,
        string? Status,
        string? LiveClassId = null,
        string? ScheduleId = null,
        string? MeetingUrl = null,
        string? ChangeReason = null,
        int Version = 1);

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
