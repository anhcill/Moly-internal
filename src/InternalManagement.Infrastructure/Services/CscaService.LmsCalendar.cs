using System.Text.Json;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        // Let users define the weekly pattern before choosing the course term.
        // UpdateClassAsync queues the saved slots once both effective dates exist.
        if (!LmsCalendarEventFactory.HasEffectiveDates(cls, schedule))
            return null;

        var now = DateTime.UtcNow;
        QueueCalendarEvent(link, archived ? "class.schedule.archived" : "class.schedule.upserted",
            LmsCalendarEventFactory.Schedule(cls, schedule, now), now);
        return null;
    }

    private async Task QueueSessionForWebAsync(
        CscaClass cls,
        CscaLessonSession session,
        bool cancelled,
        CancellationToken ct,
        string? changeReason = null,
        DateOnly? previousLessonDate = null,
        TimeSpan? previousStartTime = null,
        TimeSpan? previousEndTime = null,
        string curriculumAction = "None",
        bool extendCourse = false)
    {
        var link = await GetCalendarLinkAsync(cls, ct);
        if (link is null) return;
        var now = DateTime.UtcNow;
        QueueCalendarEvent(link, cancelled ? "class.session.cancelled" : "class.session.upserted",
            LmsCalendarEventFactory.Session(session, now, changeReason,
                previousLessonDate, previousStartTime, previousEndTime,
                curriculumAction, extendCourse), now);
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

    private Task<string?> QueueTeacherRosterForWebAsync(
        CscaClass cls,
        Employee? changedEmployee,
        CancellationToken ct,
        IReadOnlyCollection<RevokedTeacherAssignment>? revokedAssignments = null) =>
        LmsTeacherRosterSync.QueueAsync(
            _integrationDb, _currentUser, cls, changedEmployee, revokedAssignments, ct);

    /// <summary>
    /// Automatically expands Class.StartDate / Class.EndDate when a session is moved earlier
    /// (đẩy buổi lên ngày trước) or later (dời lùi) outside the original term, and queues
    /// class.upserted to LMS so the class term stays consistent with all sessions.
    /// </summary>
    private async Task ExpandClassDateRangeIfNeededAsync(CscaClass cls, DateOnly sessionDate, CancellationToken ct)
    {
        var sessionDateTime = sessionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var changed = false;
        if (cls.StartDate.HasValue && sessionDate < DateOnly.FromDateTime(cls.StartDate.Value))
        {
            cls.StartDate = sessionDateTime;
            changed = true;
        }
        if (cls.EndDate.HasValue && sessionDate > DateOnly.FromDateTime(cls.EndDate.Value))
        {
            cls.EndDate = sessionDateTime;
            changed = true;
        }
        if (changed)
        {
            cls.UpdatedAt = DateTime.UtcNow;
            cls.UpdatedBy = _currentUser.Username ?? "System";
            await QueueClassForWebAsync(cls, ct);
        }
    }

    private Task<string?> QueueClassForWebAsync(CscaClass cls, CancellationToken ct) =>
        LmsTeacherRosterSync.QueueClassAsync(_integrationDb, _currentUser, cls, ct);

    /// <summary>
    /// Best-effort immediate dispatch of newly queued LMS calendar/session outbox events
    /// so schedule/link/cancel/makeup changes reach CSCA Course LMS immediately on the first save
    /// without waiting for the background polling worker.
    /// </summary>
    private async Task TryDispatchLmsCalendarOutboxAsync(CancellationToken ct)
    {
        if (_lmsOutboxDispatcher is null) return;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
            await _lmsOutboxDispatcher.DispatchPendingAsync(batchSize: 10, timeoutCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Immediate LMS calendar outbox dispatch did not finish synchronously; the outbox item remains queued for retry.");
        }
    }
}
