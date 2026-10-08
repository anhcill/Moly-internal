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
    public async Task<IReadOnlyList<CscaScheduleDto>> GetSchedulesAsync(Guid classId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var exists = await _db.CscaClasses.AsNoTracking()
            .AnyAsync(c => c.Id == classId && !c.IsDeleted && c.CompanyId == companyId &&
                (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);
        if (!exists)
            return Array.Empty<CscaScheduleDto>();

        return await _db.CscaClassSchedules.AsNoTracking()
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .Select(s => new CscaScheduleDto
            {
                Id = s.Id,
                ClassId = s.ClassId,
                ClassroomId = s.ClassroomId,
                ClassroomName = s.Classroom != null ? s.Classroom.Name : null,
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Room = s.Room,
                MeetingUrl = s.MeetingUrl,
                Notes = s.Notes
            }).ToListAsync(ct);
    }

    public async Task<Result<CscaScheduleDto>> AddScheduleAsync(Guid classId, CreateCscaScheduleRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses.FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted && c.CompanyId == companyId &&
            (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);
        if (cls == null)
            return Result<CscaScheduleDto>.Failure("Không tìm thấy lớp học CSCA.");
        if (await HasLmsCalendarProjectionAsync(classId, ct))
            return Result<CscaScheduleDto>.Failure("Lịch của lớp này được quản lý trên LMS. Hãy tạo hoặc chỉnh sửa lịch tại LMS.");
        var validation = ValidateSchedule(request.DayOfWeek, request.StartTime, request.EndTime)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaScheduleDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, cls.CompanyId, cls.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaScheduleDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        var overlapsExistingSchedule = await _db.CscaClassSchedules.AnyAsync(s => s.ClassId == classId &&
            s.DayOfWeek == request.DayOfWeek && request.StartTime < s.EndTime && request.EndTime > s.StartTime, ct);
        if (overlapsExistingSchedule)
            return Result<CscaScheduleDto>.Failure("Khung giờ này bị chồng với một buổi khác cùng ngày trong lớp. Hãy chọn giờ không giao nhau.");
        var classroomConflict = await ValidateRecurringClassroomAvailabilityAsync(
            request.ClassroomId, classId, request.DayOfWeek, request.StartTime, request.EndTime, null, ct);
        if (classroomConflict != null)
            return Result<CscaScheduleDto>.Failure(classroomConflict);

        var schedule = new CscaClassSchedule
        {
            ClassId = classId, DayOfWeek = request.DayOfWeek, StartTime = request.StartTime,
            EndTime = request.EndTime, ClassroomId = request.ClassroomId,
            Room = classroomResult.Value?.Name ?? request.Room?.Trim(), MeetingUrl = request.MeetingUrl?.Trim(), Notes = request.Notes?.Trim()
        };
        _db.CscaClassSchedules.Add(schedule);
        var syncError = await QueueScheduleForWebAsync(cls, schedule, false, ct);
        if (syncError is not null)
            return Result<CscaScheduleDto>.Failure(syncError);
        await _db.SaveChangesAsync(ct);
        await RefreshClassScheduleStringAsync(classId, ct);
        return Result<CscaScheduleDto>.Success(ToScheduleDto(schedule));
    }

    public async Task<Result<CscaScheduleDto>> UpdateScheduleAsync(Guid classId, Guid scheduleId, UpdateCscaScheduleRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var schedule = await _db.CscaClassSchedules.Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.ClassId == classId && !s.Class.IsDeleted &&
                s.Class.CompanyId == companyId && (!businessUnitId.HasValue || s.Class.BusinessUnitId == businessUnitId), ct);
        if (schedule == null)
            return Result<CscaScheduleDto>.Failure("Không tìm thấy lịch học.");
        if (IsLmsCalendarProjection(schedule.ExternalSource))
            return Result<CscaScheduleDto>.Failure("Lịch này được quản lý trên LMS. Hãy chỉnh sửa tại LMS.");
        var validation = ValidateSchedule(request.DayOfWeek, request.StartTime, request.EndTime)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaScheduleDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, schedule.Class.CompanyId, schedule.Class.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaScheduleDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        var overlapsExistingSchedule = await _db.CscaClassSchedules.AnyAsync(s => s.Id != scheduleId && s.ClassId == classId &&
            s.DayOfWeek == request.DayOfWeek && request.StartTime < s.EndTime && request.EndTime > s.StartTime, ct);
        if (overlapsExistingSchedule)
            return Result<CscaScheduleDto>.Failure("Khung giờ này bị chồng với một buổi khác cùng ngày trong lớp. Hãy chọn giờ không giao nhau.");
        var classroomConflict = await ValidateRecurringClassroomAvailabilityAsync(
            request.ClassroomId, classId, request.DayOfWeek, request.StartTime, request.EndTime, scheduleId, ct);
        if (classroomConflict != null)
            return Result<CscaScheduleDto>.Failure(classroomConflict);

        schedule.DayOfWeek = request.DayOfWeek;
        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;
        schedule.ClassroomId = request.ClassroomId;
        schedule.Room = classroomResult.Value?.Name ?? request.Room?.Trim();
        schedule.MeetingUrl = request.MeetingUrl?.Trim();
        schedule.Notes = request.Notes?.Trim();
        var syncError = await QueueScheduleForWebAsync(schedule.Class, schedule, false, ct);
        if (syncError is not null)
            return Result<CscaScheduleDto>.Failure(syncError);
        await _db.SaveChangesAsync(ct);
        await RefreshClassScheduleStringAsync(classId, ct);
        return Result<CscaScheduleDto>.Success(ToScheduleDto(schedule));
    }

    public async Task<Result<bool>> RemoveScheduleAsync(Guid classId, Guid scheduleId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var schedule = await _db.CscaClassSchedules.Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.ClassId == classId && !s.Class.IsDeleted &&
                s.Class.CompanyId == companyId && (!businessUnitId.HasValue || s.Class.BusinessUnitId == businessUnitId), ct);
        if (schedule == null)
            return Result<bool>.Failure("Không tìm thấy lịch học.");
        if (IsLmsCalendarProjection(schedule.ExternalSource))
            return Result<bool>.Failure("Lịch này được quản lý trên LMS. Hãy ngừng lịch tại LMS.");
        var syncError = await QueueScheduleForWebAsync(schedule.Class, schedule, true, ct);
        if (syncError is not null)
            return Result<bool>.Failure(syncError);
        _db.CscaClassSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        await RefreshClassScheduleStringAsync(classId, ct);
        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<CscaClassroomDto>> GetClassroomsAsync(bool includeInactive, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var query = _db.CscaClassrooms.AsNoTracking().Where(room => room.CompanyId == companyId &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId));
        if (!includeInactive)
            query = query.Where(room => room.IsActive);

        return await query
            .OrderBy(room => room.Code)
            .Select(room => new CscaClassroomDto
            {
                Id = room.Id,
                Code = room.Code,
                Name = room.Name,
                Capacity = room.Capacity,
                Location = room.Location,
                IsActive = room.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<Result<CscaClassroomDto>> CreateClassroomAsync(CreateCscaClassroomRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var validation = ValidateClassroom(request.Code, request.Name, request.Capacity);
        if (validation != null)
            return Result<CscaClassroomDto>.Failure(validation);

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.CscaClassrooms.AnyAsync(room => room.CompanyId == companyId && room.Code == code, ct);
        if (exists)
            return Result<CscaClassroomDto>.Failure($"Mã phòng '{code}' đã tồn tại.");

        var classroom = new CscaClassroom
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = code,
            Name = request.Name.Trim(),
            Capacity = request.Capacity,
            Location = request.Location?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System"
        };
        _db.CscaClassrooms.Add(classroom);
        await _db.SaveChangesAsync(ct);
        return Result<CscaClassroomDto>.Success(ToClassroomDto(classroom));
    }

    public async Task<Result<CscaClassroomDto>> UpdateClassroomAsync(Guid classroomId, UpdateCscaClassroomRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var validation = ValidateClassroom("ROOM", request.Name, request.Capacity);
        if (validation != null)
            return Result<CscaClassroomDto>.Failure(validation.Replace("Mã phòng", "Tên phòng"));

        var classroom = await _db.CscaClassrooms.FirstOrDefaultAsync(room => room.Id == classroomId && room.CompanyId == companyId &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId), ct);
        if (classroom == null)
            return Result<CscaClassroomDto>.Failure("Không tìm thấy phòng học.");

        classroom.Name = request.Name.Trim();
        classroom.Capacity = request.Capacity;
        classroom.Location = request.Location?.Trim();
        classroom.IsActive = request.IsActive;
        classroom.UpdatedAt = DateTime.UtcNow;
        classroom.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<CscaClassroomDto>.Success(ToClassroomDto(classroom));
    }

    public async Task<Result<bool>> RemoveClassroomAsync(Guid classroomId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var classroom = await _db.CscaClassrooms.FirstOrDefaultAsync(room => room.Id == classroomId && room.CompanyId == companyId &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId), ct);
        if (classroom == null)
            return Result<bool>.Failure("Không tìm thấy phòng học.");

        var isInUse = await _db.CscaClassSchedules.AnyAsync(schedule => schedule.ClassroomId == classroomId, ct)
            || await _db.CscaLessonSessions.AnyAsync(session => session.ClassroomId == classroomId, ct);
        if (isInUse)
            return Result<bool>.Failure("Phòng đang được dùng trong lịch học hoặc buổi học. Hãy chuyển sang Không hoạt động thay vì xóa.");

        classroom.IsDeleted = true;
        classroom.DeletedAt = DateTime.UtcNow;
        classroom.DeletedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<CscaLessonSessionDto>> GetLessonSessionsAsync(
        Guid classId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Array.Empty<CscaLessonSessionDto>();

        var query = _db.CscaLessonSessions.AsNoTracking()
            .Include(session => session.Classroom)
            .Where(session => session.ClassId == classId);
        if (fromDate.HasValue) query = query.Where(session => session.LessonDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(session => session.LessonDate <= toDate.Value);

        return await query.OrderBy(session => session.LessonDate).ThenBy(session => session.StartTime)
            .Select(session => new CscaLessonSessionDto
            {
                Id = session.Id,
                ClassId = session.ClassId,
                ScheduleId = session.ScheduleId,
                ClassroomId = session.ClassroomId,
                ClassroomName = session.Classroom != null ? session.Classroom.Name : null,
                LessonDate = session.LessonDate,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                Status = session.Status,
                MeetingUrl = session.MeetingUrl,
                Notes = session.Notes
            }).ToListAsync(ct);
    }

    public async Task<Result<CscaLessonSessionDto>> CreateLessonSessionAsync(
        Guid classId, CreateCscaLessonSessionRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses.FirstOrDefaultAsync(item => item.Id == classId && item.CompanyId == companyId && !item.IsDeleted &&
            (!businessUnitId.HasValue || item.BusinessUnitId == businessUnitId), ct);
        if (cls == null)
            return Result<CscaLessonSessionDto>.Failure("Không tìm thấy lớp học CSCA.");
        if (await HasLmsCalendarProjectionAsync(classId, ct))
            return Result<CscaLessonSessionDto>.Failure("Buổi học của lớp này được quản lý trên LMS. Hãy tạo buổi tại LMS.");
        var validation = ValidateLessonSession(request.LessonDate, request.StartTime, request.EndTime, request.Status)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaLessonSessionDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, cls.CompanyId, cls.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaLessonSessionDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        if (request.ScheduleId.HasValue && !await _db.CscaClassSchedules.AnyAsync(schedule => schedule.Id == request.ScheduleId.Value && schedule.ClassId == classId, ct))
            return Result<CscaLessonSessionDto>.Failure("Lịch tuần gốc không thuộc lớp này.");
        var conflict = await ValidateDatedSessionAvailabilityAsync(request.ClassroomId, classId, request.LessonDate, request.StartTime, request.EndTime, null, request.Status, ct);
        if (conflict != null)
            return Result<CscaLessonSessionDto>.Failure(conflict);

        var session = new CscaLessonSession
        {
            ClassId = classId,
            ScheduleId = request.ScheduleId,
            ClassroomId = request.ClassroomId,
            LessonDate = request.LessonDate,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = request.Status.Trim(),
            MeetingUrl = request.MeetingUrl?.Trim(),
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System",
            Classroom = classroomResult.Value
        };
        _db.CscaLessonSessions.Add(session);
        await QueueSessionForWebAsync(cls, session, false, ct);
        await _db.SaveChangesAsync(ct);
        return Result<CscaLessonSessionDto>.Success(ToLessonSessionDto(session));
    }

    public async Task<Result<CscaLessonSessionDto>> UpdateLessonSessionAsync(
        Guid classId, Guid sessionId, UpdateCscaLessonSessionRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaLessonSessionDto>.Failure("Không tìm thấy lớp học CSCA.");

        var session = await _db.CscaLessonSessions.Include(item => item.Class).Include(item => item.Classroom)
            .FirstOrDefaultAsync(item => item.Id == sessionId && item.ClassId == classId, ct);
        if (session == null)
            return Result<CscaLessonSessionDto>.Failure("Không tìm thấy buổi học.");
        if (IsLmsCalendarProjection(session.ExternalSource))
            return Result<CscaLessonSessionDto>.Failure("Buổi học này được quản lý trên LMS. Hãy chỉnh sửa tại LMS.");
        var validation = ValidateLessonSession(request.LessonDate, request.StartTime, request.EndTime, request.Status)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaLessonSessionDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, session.Class.CompanyId, session.Class.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaLessonSessionDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        var conflict = await ValidateDatedSessionAvailabilityAsync(request.ClassroomId, classId, request.LessonDate, request.StartTime, request.EndTime, sessionId, request.Status, ct);
        if (conflict != null)
            return Result<CscaLessonSessionDto>.Failure(conflict);

        session.ClassroomId = request.ClassroomId;
        session.Classroom = classroomResult.Value;
        session.LessonDate = request.LessonDate;
        session.StartTime = request.StartTime;
        session.EndTime = request.EndTime;
        session.Status = request.Status.Trim();
        session.MeetingUrl = request.MeetingUrl?.Trim();
        session.Notes = request.Notes?.Trim();
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedBy = _currentUser.Username ?? "System";
        await QueueSessionForWebAsync(session.Class, session,
            string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase), ct);
        await _db.SaveChangesAsync(ct);
        return Result<CscaLessonSessionDto>.Success(ToLessonSessionDto(session));
    }

    public async Task<Result<bool>> RemoveLessonSessionAsync(Guid classId, Guid sessionId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA.");
        var session = await _db.CscaLessonSessions.Include(item => item.Class)
            .FirstOrDefaultAsync(item => item.Id == sessionId && item.ClassId == classId, ct);
        if (session == null)
            return Result<bool>.Failure("Không tìm thấy buổi học.");
        if (IsLmsCalendarProjection(session.ExternalSource))
            return Result<bool>.Failure("Buổi học này được quản lý trên LMS. Hãy hủy buổi tại LMS.");

        await QueueSessionForWebAsync(session.Class, session, true, ct);
        _db.CscaLessonSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> GenerateLessonSessionsAsync(Guid classId, GenerateCscaLessonSessionsRequest request, CancellationToken ct)
    {
        if (request.FromDate > request.ToDate)
            return Result<int>.Failure("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        if (request.ToDate.DayNumber - request.FromDate.DayNumber > 366)
            return Result<int>.Failure("Chỉ được tạo buổi học tối đa trong 366 ngày cho một lần thao tác.");
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<int>.Failure("Không tìm thấy lớp học CSCA.");
        if (await HasLmsCalendarProjectionAsync(classId, ct))
            return Result<int>.Failure("Lớp này được LMS tự sinh buổi học theo lịch cố định. Không thể sinh thêm từ Management.");

        var schedules = await _db.CscaClassSchedules.AsNoTracking()
            .Where(schedule => schedule.ClassId == classId)
            .OrderBy(schedule => schedule.DayOfWeek).ThenBy(schedule => schedule.StartTime)
            .ToListAsync(ct);
        if (schedules.Count == 0)
            return Result<int>.Failure("Lớp chưa có lịch tuần. Hãy tạo lịch tuần trước khi sinh buổi học.");

        var existing = await _db.CscaLessonSessions
            .Where(session => session.LessonDate >= request.FromDate && session.LessonDate <= request.ToDate)
            .ToListAsync(ct);
        var generated = new List<CscaLessonSession>();
        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            foreach (var schedule in schedules.Where(item => item.DayOfWeek == (int)date.DayOfWeek))
            {
                if (existing.Any(session => session.ScheduleId == schedule.Id && session.LessonDate == date))
                    continue;
                var hasClassConflict = existing.Concat(generated).Any(session => session.ClassId == classId && session.LessonDate == date &&
                    !string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) && schedule.StartTime < session.EndTime && schedule.EndTime > session.StartTime);
                if (hasClassConflict)
                    return Result<int>.Failure($"Không thể tạo buổi ngày {date:dd/MM/yyyy} vì bị chồng với buổi khác của lớp.");
                var hasRoomConflict = schedule.ClassroomId.HasValue && existing.Concat(generated).Any(session => session.ClassroomId == schedule.ClassroomId &&
                    session.ClassId != classId && session.LessonDate == date && !string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    schedule.StartTime < session.EndTime && schedule.EndTime > session.StartTime);
                if (hasRoomConflict)
                    return Result<int>.Failure($"Phòng đã có lớp khác dùng vào {date:dd/MM/yyyy}, {schedule.StartTime:hh\\:mm} - {schedule.EndTime:hh\\:mm}.");

                generated.Add(new CscaLessonSession
                {
                    ClassId = classId,
                    ScheduleId = schedule.Id,
                    ClassroomId = schedule.ClassroomId,
                    LessonDate = date,
                    StartTime = schedule.StartTime,
                    EndTime = schedule.EndTime,
                    Status = "Scheduled",
                    MeetingUrl = schedule.MeetingUrl,
                    Notes = schedule.Notes,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username ?? "System"
                });
            }
        }

        if (generated.Count > 0)
        {
            _db.CscaLessonSessions.AddRange(generated);
            var cls = await _db.CscaClasses.FirstAsync(item => item.Id == classId, ct);
            foreach (var session in generated)
                await QueueSessionForWebAsync(cls, session, false, ct);
            await _db.SaveChangesAsync(ct);
        }
        return Result<int>.Success(generated.Count);
    }

    public async Task<Result<IReadOnlyList<CscaLessonAttendanceDto>>> GetLessonAttendanceAsync(
        Guid classId, Guid sessionId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<IReadOnlyList<CscaLessonAttendanceDto>>.Failure("Không tìm thấy lớp học CSCA.");
        var sessionExists = await _db.CscaLessonSessions.AnyAsync(session => session.Id == sessionId && session.ClassId == classId, ct);
        if (!sessionExists)
            return Result<IReadOnlyList<CscaLessonAttendanceDto>>.Failure("Không tìm thấy buổi học.");

        var students = await _db.CscaClassStudents.AsNoTracking().Where(student => student.ClassId == classId)
            .OrderBy(student => student.StudentName).ToListAsync(ct);
        var recorded = await _db.CscaLessonAttendances.AsNoTracking().Where(attendance => attendance.LessonSessionId == sessionId)
            .ToDictionaryAsync(attendance => attendance.StudentId, ct);
        var items = students.Select(student => recorded.TryGetValue(student.Id, out var attendance)
            ? ToAttendanceDto(attendance, student.StudentName)
            : new CscaLessonAttendanceDto { LessonSessionId = sessionId, StudentId = student.Id, StudentName = student.StudentName })
            .ToList();
        return Result<IReadOnlyList<CscaLessonAttendanceDto>>.Success(items);
    }

    public async Task<Result<CscaLessonAttendanceDto>> UpsertLessonAttendanceAsync(
        Guid classId, Guid sessionId, UpsertCscaLessonAttendanceRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaLessonAttendanceDto>.Failure("Không tìm thấy lớp học CSCA.");
        if (!IsValidAttendanceStatus(request.Status))
            return Result<CscaLessonAttendanceDto>.Failure("Trạng thái điểm danh không hợp lệ.");

        var session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item => item.Id == sessionId && item.ClassId == classId, ct);
        if (session == null)
            return Result<CscaLessonAttendanceDto>.Failure("Không tìm thấy buổi học.");
        if (string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return Result<CscaLessonAttendanceDto>.Failure("Không thể điểm danh cho buổi học đã hủy.");
        var student = await _db.CscaClassStudents.FirstOrDefaultAsync(item => item.Id == request.StudentId && item.ClassId == classId, ct);
        if (student == null)
            return Result<CscaLessonAttendanceDto>.Failure("Học viên không thuộc lớp của buổi học này.");

        var attendance = await _db.CscaLessonAttendances.FirstOrDefaultAsync(item => item.LessonSessionId == sessionId && item.StudentId == request.StudentId, ct);
        if (attendance == null)
        {
            attendance = new CscaLessonAttendance
            {
                LessonSessionId = sessionId,
                StudentId = request.StudentId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username ?? "System"
            };
            _db.CscaLessonAttendances.Add(attendance);
        }
        attendance.Status = request.Status.Trim();
        attendance.CheckInAt = request.CheckInAt;
        attendance.Notes = request.Notes?.Trim();
        attendance.UpdatedAt = DateTime.UtcNow;
        attendance.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<CscaLessonAttendanceDto>.Success(ToAttendanceDto(attendance, student.StudentName));
    }

}
