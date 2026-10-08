namespace InternalManagement.Application.Features.CscaInterview.DTOs;

/// <summary>Read-only attendance received from the linked CSCA Course website.</summary>
public sealed record CscaClassAttendanceReportDto(
    Guid ClassId,
    int TotalStudents,
    int TotalSessions,
    int TotalRecords,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int ExcusedCount,
    decimal AttendanceRatePercent,
    DateTime? LastSyncedAt,
    IReadOnlyList<CscaAttendanceSessionSummaryDto> Sessions,
    IReadOnlyList<CscaStudentAttendanceSummaryDto> Students);

public sealed record CscaAttendanceSessionSummaryDto(
    Guid SessionId,
    DateOnly LessonDate,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int TotalRecords,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int ExcusedCount,
    DateTime? LastSyncedAt);

public sealed record CscaStudentAttendanceSummaryDto(
    Guid StudentId,
    string StudentName,
    int TotalRecords,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int ExcusedCount,
    decimal AttendanceRatePercent);
