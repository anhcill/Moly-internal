using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Entities.Integration;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService
{
    public async Task<Result<CscaClassAttendanceReportDto>> GetAttendanceReportAsync(
        Guid classId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaClassAttendanceReportDto>.Failure("Không tìm thấy lớp học CSCA.");
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            return Result<CscaClassAttendanceReportDto>.Failure("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");

        var students = await _db.CscaClassStudents.AsNoTracking()
            .Where(student => student.ClassId == classId)
            .OrderBy(student => student.StudentName)
            .Select(student => new { student.Id, student.StudentName })
            .ToListAsync(ct);

        var query = _db.CscaLessonAttendances.AsNoTracking()
            .Where(attendance => attendance.LessonSession.ClassId == classId &&
                attendance.LessonSession.ExternalSource == LmsIntegrationSourceSystems.CscaCourseLms);
        if (fromDate.HasValue)
            query = query.Where(attendance => attendance.LessonSession.LessonDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(attendance => attendance.LessonSession.LessonDate <= toDate.Value);

        var rows = await query.Select(attendance => new
        {
            attendance.StudentId,
            attendance.Status,
            attendance.CreatedAt,
            attendance.UpdatedAt,
            SessionId = attendance.LessonSessionId,
            attendance.LessonSession.LessonDate,
            attendance.LessonSession.StartTime,
            attendance.LessonSession.EndTime
        }).ToListAsync(ct);

        var sessions = rows.GroupBy(row => new
            { row.SessionId, row.LessonDate, row.StartTime, row.EndTime })
            .OrderByDescending(group => group.Key.LessonDate)
            .ThenByDescending(group => group.Key.StartTime)
            .Select(group => new CscaAttendanceSessionSummaryDto(
                group.Key.SessionId,
                group.Key.LessonDate,
                group.Key.StartTime,
                group.Key.EndTime,
                group.Count(),
                group.Count(row => row.Status == "Present"),
                group.Count(row => row.Status == "Late"),
                group.Count(row => row.Status == "Absent"),
                group.Count(row => row.Status == "Excused"),
                group.Max(row => row.UpdatedAt ?? row.CreatedAt)))
            .ToList();

        var byStudent = rows.GroupBy(row => row.StudentId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var studentSummaries = students.Select(student =>
        {
            var items = byStudent.GetValueOrDefault(student.Id);
            var present = items?.Count(row => row.Status == "Present") ?? 0;
            var late = items?.Count(row => row.Status == "Late") ?? 0;
            var absent = items?.Count(row => row.Status == "Absent") ?? 0;
            var excused = items?.Count(row => row.Status == "Excused") ?? 0;
            return new CscaStudentAttendanceSummaryDto(student.Id, student.StudentName,
                items?.Count ?? 0, present, late, absent, excused,
                AttendanceRate(present, late, absent));
        }).ToList();

        var presentCount = rows.Count(row => row.Status == "Present");
        var lateCount = rows.Count(row => row.Status == "Late");
        var absentCount = rows.Count(row => row.Status == "Absent");
        var report = new CscaClassAttendanceReportDto(
            classId,
            students.Count,
            sessions.Count,
            rows.Count,
            presentCount,
            lateCount,
            absentCount,
            rows.Count(row => row.Status == "Excused"),
            AttendanceRate(presentCount, lateCount, absentCount),
            rows.Count == 0 ? null : rows.Max(row => row.UpdatedAt ?? row.CreatedAt),
            sessions,
            studentSummaries);
        return Result<CscaClassAttendanceReportDto>.Success(report);
    }

    // Excused attendance is excluded from the rate denominator.
    private static decimal AttendanceRate(int present, int late, int absent)
    {
        var eligible = present + late + absent;
        return eligible == 0 ? 0 : Math.Round((present + late) * 100m / eligible, 1);
    }
}
