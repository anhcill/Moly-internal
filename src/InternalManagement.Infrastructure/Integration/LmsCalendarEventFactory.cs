using System.Globalization;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Integration;

namespace InternalManagement.Infrastructure.Integration;

internal static class LmsCalendarEventFactory
{
    private const string Timezone = "Asia/Ho_Chi_Minh";

    internal static bool IsValidSession(CscaLessonSession session) =>
        session.LessonDate != default &&
        session.StartTime >= TimeSpan.Zero &&
        session.StartTime < TimeSpan.FromDays(1) &&
        session.EndTime > session.StartTime &&
        session.EndTime <= TimeSpan.FromDays(1);

    internal static bool HasEffectiveDates(CscaClass cls, CscaClassSchedule schedule)
    {
        var start = schedule.StartDate ?? ToDate(cls.StartDate);
        var end = schedule.EndDate ?? ToDate(cls.EndDate);
        return start.HasValue && end.HasValue && end.Value >= start.Value;
    }

    internal static object Schedule(CscaClass cls, CscaClassSchedule schedule, DateTime updatedAt)
    {
        var start = schedule.StartDate ?? ToDate(cls.StartDate);
        var end = schedule.EndDate ?? ToDate(cls.EndDate);
        if (!start.HasValue || !end.HasValue || end.Value < start.Value)
            throw new InvalidOperationException($"Lớp {cls.Code} thiếu thời hạn hợp lệ để đồng bộ lịch cố định.");

        return new
        {
            ClassSourceId = cls.Id.ToString("N"),
            ScheduleSourceId = schedule.Id.ToString("N"),
            LmsScheduleId = schedule.ExternalScheduleId,
            Title = string.IsNullOrWhiteSpace(schedule.Title) ? cls.Name : schedule.Title,
            schedule.DayOfWeek,
            StartTime = Clock(schedule.StartTime),
            EndTime = Clock(schedule.EndTime),
            StartDate = start.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = end.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Timezone,
            Status = string.Equals(schedule.Status, "Archived", StringComparison.OrdinalIgnoreCase)
                ? "archived" : "active",
            schedule.MeetingUrl,
            SourceUpdatedAt = updatedAt
        };
    }

    internal static object Session(CscaLessonSession session, DateTime updatedAt, string? changeReason = null) => new
    {
        ClassSourceId = session.ClassId.ToString("N"),
        SessionSourceId = session.Id.ToString("N"),
        LmsSessionId = session.ExternalSessionId,
        ExpectedLmsVersion = session.ExternalVersion > 0 ? session.ExternalVersion : (int?)null,
        ScheduleSourceId = session.Schedule?.ExternalSource == LmsIntegrationSourceSystems.CscaCourseLms
            ? null : session.ScheduleId?.ToString("N"),
        LmsScheduleId = session.Schedule?.ExternalSource == LmsIntegrationSourceSystems.CscaCourseLms
            ? session.Schedule.ExternalScheduleId : null,
        LessonDate = session.LessonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        StartTime = Clock(session.StartTime),
        EndTime = Clock(session.EndTime),
        Timezone,
        Status = string.Equals(session.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            ? "ended" : session.Status.Trim().ToLowerInvariant(),
        session.MeetingUrl,
        ChangeReason = changeReason,
        SourceUpdatedAt = updatedAt
    };

    private static DateOnly? ToDate(DateTime? value) => value.HasValue ? DateOnly.FromDateTime(value.Value) : null;

    private static string Clock(TimeSpan value) => value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
