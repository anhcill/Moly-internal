using System.Text.Json;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService
{
    private static readonly JsonSerializerOptions CalendarJsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<LmsCourseLink?> GetCalendarLinkAsync(CscaClass cls, CancellationToken ct) =>
        _integrationDb is null ? null : await _integrationDb.LmsCourseLinks.FirstOrDefaultAsync(link =>
            link.CompanyId == cls.CompanyId && link.CourseId == cls.CourseId &&
            link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            link.Status != IntegrationStatus.Pending, ct);

    private async Task<string?> QueueScheduleForWebAsync(
        CscaClass cls, CscaClassSchedule schedule, bool archived, CancellationToken ct)
    {
        if (IsLmsCalendarProjection(schedule.ExternalSource)) return null;
        var link = await GetCalendarLinkAsync(cls, ct);
        if (link is null) return null;
        if (!LmsCalendarEventFactory.HasEffectiveDates(cls, schedule))
            return $"Lớp {cls.Code} chưa có ngày bắt đầu và kết thúc hợp lệ; không thể đẩy lịch cố định sang Web.";

        var now = DateTime.UtcNow;
        QueueCalendarEvent(link, archived ? "class.schedule.archived" : "class.schedule.upserted",
            LmsCalendarEventFactory.Schedule(cls, schedule, now), now);
        return null;
    }

    private async Task QueueSessionForWebAsync(CscaClass cls, CscaLessonSession session, bool cancelled, CancellationToken ct, string? changeReason = null)
    {
        var link = await GetCalendarLinkAsync(cls, ct);
        if (link is null) return;
        var now = DateTime.UtcNow;
        QueueCalendarEvent(link, cancelled ? "class.session.cancelled" : "class.session.upserted",
            LmsCalendarEventFactory.Session(session, now, changeReason), now);
    }

    private void QueueCalendarEvent(LmsCourseLink link, string eventType, object payload, DateTime now)
        => QueueLinkedEvent(link, eventType, payload, now, Guid.NewGuid().ToString("N"));

    private void QueueLinkedEvent(
        LmsCourseLink link, string eventType, object payload, DateTime now, string correlationId)
    {
        if (_integrationDb is null) return;
        var eventId = Guid.NewGuid().ToString("N");
        var sequence = _integrationDb.IntegrationOutboxes.Local.Count(item =>
            item.CorrelationId == correlationId);
        var command = new LmsManagementEvent(eventId, eventType, now, "internal-management", payload);
        _integrationDb.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = link.CompanyId,
            BusinessUnitId = link.BusinessUnitId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = eventId,
            EventType = LmsOutboxEventTypes.ManagementEventRequested,
            AggregateType = nameof(LmsCourseLink),
            AggregateId = link.Id.ToString("N"),
            IdempotencyKey = $"management-event:{eventId}",
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(command, CalendarJsonOptions),
            Status = IntegrationStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now.AddMilliseconds(sequence),
            CreatedBy = _currentUser.Username ?? "System"
        });
        if (link.Status != IntegrationStatus.Success)
            link.Status = IntegrationStatus.Processing;
        link.LastSyncError = null;
        link.LastSyncedAt = null;
        link.UpdatedAt = now;
        link.UpdatedBy = _currentUser.Username ?? "System";
    }

    private async Task<string?> QueueTeacherRosterForWebAsync(
        CscaClass cls, Employee? changedEmployee, CancellationToken ct)
    {
        var link = await GetCalendarLinkAsync(cls, ct);
        if (link is null) return null;

        var lead = cls.Staff.FirstOrDefault(item =>
            string.Equals(item.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase) &&
            item.Employee is { IsDeleted: false } &&
            !string.Equals(item.Employee.Status, "Resigned", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Employee.Email));
        if (lead is null)
            return "Lớp đã liên kết Web nên phải có ít nhất một giáo viên với email hợp lệ. Hãy phân công giáo viên thay thế trước.";

        var now = DateTime.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");
        if (changedEmployee is not null &&
            cls.Staff.Any(item => item.EmployeeId == changedEmployee.Id &&
                string.Equals(item.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase)) &&
            !changedEmployee.IsDeleted &&
            !string.Equals(changedEmployee.Status, "Resigned", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(changedEmployee.Email))
        {
            QueueLinkedEvent(link, "teacher.upserted", new
            {
                TeacherSourceId = changedEmployee.Id.ToString("N"),
                changedEmployee.FullName,
                changedEmployee.Email,
                AccountStatus = "active",
                SourceUpdatedAt = now
            }, now, correlationId);
        }

        QueueLinkedEvent(link, "class.upserted", new
        {
            ClassSourceId = cls.Id.ToString("N"),
            CourseSourceId = link.ExternalCourseId,
            Title = cls.Name,
            Description = cls.Batch,
            MaxStudents = Math.Max(30, cls.Students.Count),
            Status = string.Equals(cls.Status, "Completed", StringComparison.OrdinalIgnoreCase) ? "completed" :
                string.Equals(cls.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "active",
            LeadTeacherSourceId = lead.EmployeeId.ToString("N"),
            SourceUpdatedAt = now
        }, now, correlationId);
        return null;
    }
}
