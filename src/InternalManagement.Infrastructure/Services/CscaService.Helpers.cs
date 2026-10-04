using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService
{
    private static string DayShortLabel(int dayOfWeek) => dayOfWeek switch
    {
        1 => "T2",
        2 => "T3",
        3 => "T4",
        4 => "T5",
        5 => "T6",
        6 => "T7",
        0 => "CN",
        _ => $"T{dayOfWeek + 1}"
    };

    public static string FormatScheduleSummary(IEnumerable<CscaClassSchedule> schedules)
    {
        var list = schedules
            .OrderBy(s => s.DayOfWeek == 0 ? 7 : s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToList();

        if (list.Count == 0) return string.Empty;

        var timeGroups = list
            .GroupBy(s => (s.StartTime, s.EndTime))
            .ToList();

        var groupSummaries = new List<string>();
        foreach (var group in timeGroups)
        {
            var dayNames = group.Select(s => DayShortLabel(s.DayOfWeek)).Distinct().ToList();
            var daysPart = dayNames.Count switch
            {
                1 => dayNames[0],
                2 => $"{dayNames[0]} - {dayNames[1]}",
                _ => string.Join(", ", dayNames)
            };

            var startStr = group.Key.StartTime.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
            var endStr = group.Key.EndTime.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
            groupSummaries.Add($"{daysPart} ({startStr} - {endStr})");
        }

        return string.Join(", ", groupSummaries);
    }

    private async Task RefreshClassScheduleStringAsync(Guid classId, CancellationToken ct)
    {
        var cls = await _db.CscaClasses
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted, ct);

        if (cls != null)
        {
            cls.Schedule = FormatScheduleSummary(cls.Schedules);
            cls.UpdatedAt = DateTime.UtcNow;
            cls.UpdatedBy = _currentUser.Username ?? "System";
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string? ValidateSchedule(int dayOfWeek, TimeSpan startTime, TimeSpan endTime)
    {
        if (dayOfWeek is < 0 or > 6)
            return "Ngày trong tuần phải nằm trong khoảng 0 (Chủ nhật) đến 6 (Thứ bảy).";
        if (startTime >= endTime)
            return "Giờ bắt đầu phải trước giờ kết thúc.";
        return null;
    }

    private static string? ValidateMeetingUrl(string? meetingUrl)
    {
        if (string.IsNullOrWhiteSpace(meetingUrl))
            return null;

        return Uri.TryCreate(meetingUrl.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? null
            : "Link học trực tuyến phải bắt đầu bằng http:// hoặc https://.";
    }

    private static string? ValidateClassroom(string code, string name, int? capacity)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Mã phòng là bắt buộc.";
        if (string.IsNullOrWhiteSpace(name))
            return "Tên phòng là bắt buộc.";
        if (capacity is < 1)
            return "Sức chứa phải lớn hơn 0.";
        return null;
    }

    private static string? ValidateLessonSession(DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, string? status)
    {
        if (lessonDate == default)
            return "Ngày học là bắt buộc.";
        if (startTime >= endTime)
            return "Giờ bắt đầu phải trước giờ kết thúc.";
        return IsValidLessonStatus(status)
            ? null
            : "Trạng thái buổi học phải là Đã lên lịch, Đổi lịch, Đã hủy hoặc Hoàn thành.";
    }

    private static bool IsValidLessonStatus(string? status) => new[] { "Scheduled", "Rescheduled", "Cancelled", "Completed" }
        .Contains(status?.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsValidAttendanceStatus(string? status) => new[] { "Present", "Late", "Absent", "Excused" }
        .Contains(status?.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsLmsCalendarProjection(string? sourceSystem) =>
        string.Equals(sourceSystem, LmsIntegrationSourceSystems.CscaCourseLms, StringComparison.Ordinal);

    private Task<bool> HasLmsCalendarProjectionAsync(Guid classId, CancellationToken ct) =>
        _db.CscaClassSchedules.AnyAsync(schedule => schedule.ClassId == classId &&
            schedule.ExternalSource == LmsIntegrationSourceSystems.CscaCourseLms, ct);

    private async Task<Result<CscaClassroom?>> ResolveClassroomAsync(
        Guid? classroomId, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        if (!classroomId.HasValue)
            return Result<CscaClassroom?>.Success(null);

        var classroom = await _db.CscaClassrooms.FirstOrDefaultAsync(room => room.Id == classroomId.Value &&
            room.CompanyId == companyId && room.IsActive &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId), ct);
        return classroom is null
            ? Result<CscaClassroom?>.Failure("Phòng học không tồn tại, đang ngừng dùng hoặc không thuộc đơn vị hiện tại.")
            : Result<CscaClassroom?>.Success(classroom);
    }

    private async Task<string?> ValidateRecurringClassroomAvailabilityAsync(
        Guid? classroomId, Guid classId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, Guid? excludedScheduleId, CancellationToken ct)
    {
        if (!classroomId.HasValue)
            return null;

        var hasConflict = await _db.CscaClassSchedules.AnyAsync(schedule => schedule.ClassroomId == classroomId.Value &&
            schedule.ClassId != classId && schedule.DayOfWeek == dayOfWeek &&
            (!excludedScheduleId.HasValue || schedule.Id != excludedScheduleId.Value) &&
            startTime < schedule.EndTime && endTime > schedule.StartTime && schedule.Class.Status == "Active", ct);
        return hasConflict
            ? "Phòng học đã được một lớp khác dùng ở khung giờ này trong lịch tuần. Hãy chọn phòng hoặc giờ khác."
            : null;
    }

    private async Task<string?> ValidateDatedSessionAvailabilityAsync(
        Guid? classroomId, Guid classId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? excludedSessionId, string status, CancellationToken ct)
    {
        if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return null;

        var classConflict = await _db.CscaLessonSessions.AnyAsync(session => session.ClassId == classId && session.LessonDate == lessonDate &&
            (!excludedSessionId.HasValue || session.Id != excludedSessionId.Value) && session.Status != "Cancelled" &&
            startTime < session.EndTime && endTime > session.StartTime, ct);
        if (classConflict)
            return "Buổi học bị chồng giờ với một buổi khác của lớp trong ngày này.";
        if (!classroomId.HasValue)
            return null;

        var classroomConflict = await _db.CscaLessonSessions.AnyAsync(session => session.ClassroomId == classroomId.Value && session.ClassId != classId &&
            session.LessonDate == lessonDate && (!excludedSessionId.HasValue || session.Id != excludedSessionId.Value) &&
            session.Status != "Cancelled" && startTime < session.EndTime && endTime > session.StartTime, ct);
        return classroomConflict
            ? "Phòng học đã được một lớp khác sử dụng trong khung giờ này."
            : null;
    }

    private static string? ValidateStaffAssignment(string? roleInClass, decimal compensationRate)
    {
        if (string.IsNullOrWhiteSpace(roleInClass))
            return "Vai trò của giáo viên / nhân sự là bắt buộc.";
        if (compensationRate < 0)
            return "Mức thù lao không được âm.";

        var allowedRoles = new[] { "Teacher", "TeachingAssistant", "Mentor" };
        return allowedRoles.Contains(roleInClass.Trim(), StringComparer.OrdinalIgnoreCase)
            ? null
            : "Vai trò phải là Giáo viên, Trợ giảng hoặc Mentor.";
    }

    private static CscaScheduleDto ToScheduleDto(CscaClassSchedule schedule) => new()
    {
        Id = schedule.Id, ClassId = schedule.ClassId, DayOfWeek = schedule.DayOfWeek,
        ClassroomId = schedule.ClassroomId, ClassroomName = schedule.Classroom?.Name,
        StartTime = schedule.StartTime, EndTime = schedule.EndTime, Room = schedule.Room,
        MeetingUrl = schedule.MeetingUrl, Notes = schedule.Notes
    };

    private static CscaClassroomDto ToClassroomDto(CscaClassroom classroom) => new()
    {
        Id = classroom.Id,
        Code = classroom.Code,
        Name = classroom.Name,
        Capacity = classroom.Capacity,
        Location = classroom.Location,
        IsActive = classroom.IsActive
    };

    private static CscaLessonSessionDto ToLessonSessionDto(CscaLessonSession session) => new()
    {
        Id = session.Id,
        ClassId = session.ClassId,
        ScheduleId = session.ScheduleId,
        ClassroomId = session.ClassroomId,
        ClassroomName = session.Classroom?.Name,
        LessonDate = session.LessonDate,
        StartTime = session.StartTime,
        EndTime = session.EndTime,
        Status = session.Status,
        MeetingUrl = session.MeetingUrl,
        Notes = session.Notes
    };

    private static CscaLessonAttendanceDto ToAttendanceDto(CscaLessonAttendance attendance, string studentName) => new()
    {
        Id = attendance.Id,
        LessonSessionId = attendance.LessonSessionId,
        StudentId = attendance.StudentId,
        StudentName = studentName,
        Status = attendance.Status,
        CheckInAt = attendance.CheckInAt,
        Notes = attendance.Notes
    };

    private async Task<bool> IsClassInScopeAsync(Guid classId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        return await _db.CscaClasses.AsNoTracking().AnyAsync(c => c.Id == classId && !c.IsDeleted &&
            c.CompanyId == companyId && (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);
    }

    private async Task<Result<Domain.Entities.EdTech.Course>> ResolveCourseAsync(
        Guid? requestedCourseId, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        if (requestedCourseId.HasValue)
        {
            var selected = await _db.Courses.FirstOrDefaultAsync(c => c.Id == requestedCourseId.Value &&
                c.CompanyId == companyId && !c.IsDeleted, ct);
            return selected is null
                ? Result<Domain.Entities.EdTech.Course>.Failure("Khóa học không tồn tại hoặc không thuộc mảng hiện tại.")
                : Result<Domain.Entities.EdTech.Course>.Success(selected);
        }

        // Backward-compatible fallback for old clients/tests. The desktop flow always requires
        // the operator to select an existing course before creating a class.
        const string legacySourceId = "LEGACY-CSCA-COURSE";
        var legacy = await _db.Courses.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CourseSourceId == legacySourceId, ct);
        if (legacy != null)
            return Result<Domain.Entities.EdTech.Course>.Success(legacy);

        legacy = new Domain.Entities.EdTech.Course
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            CourseSourceId = legacySourceId,
            Title = "Khóa học CSCA chưa phân loại",
            Description = "Khóa học tạm dùng để tương thích dữ liệu lớp học cũ.",
            Price = 0,
            Status = "Draft",
            CreatedBy = _currentUser.Username ?? "System"
        };
        _db.Courses.Add(legacy);
        await _db.SaveChangesAsync(ct);
        return Result<Domain.Entities.EdTech.Course>.Success(legacy);
    }

    private async Task SyncClassProfitAllocationAsync(CscaClass cls, CancellationToken ct)
    {
        var allocation = await _db.ProfitAllocations
            .FirstOrDefaultAsync(p => p.ReferenceType == "CscaClass" && p.ReferenceId == cls.Id, ct);

        var actualRevenue = cls.Students.Sum(s => s.PaidAmount);
        var totalStaffExpense = cls.Staff?.Sum(st => st.CompensationRate) ?? 0;

        if (allocation == null)
        {
            allocation = new ProfitAllocation
            {
                CompanyId = cls.CompanyId,
                BusinessUnitId = cls.BusinessUnitId,
                ReferenceType = "CscaClass",
                ReferenceId = cls.Id,
                IncomeAmount = actualRevenue,
                ExpenseAmount = totalStaffExpense,
                AllocatedAt = DateTime.UtcNow
            };
            _db.ProfitAllocations.Add(allocation);
        }
        else
        {
            allocation.IncomeAmount = actualRevenue;
            allocation.ExpenseAmount = totalStaffExpense;
            allocation.AllocatedAt = DateTime.UtcNow;
        }
    }
}
