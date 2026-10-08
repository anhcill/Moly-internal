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
    public async Task<PaginatedResult<CscaClassDto>> GetClassesAsync(
        string? search, string? batch, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, _) = await GetContextAsync(ct);
        var query = _db.CscaClasses
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.CompanyId == companyId)
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.Code.ToLower().Contains(s) || c.Name.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(batch))
        {
            query = query.Where(c => c.Batch == batch);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CscaClassDto
            {
                Id = c.Id,
                CourseId = c.CourseId,
                CourseTitle = c.Course.Title,
                Code = c.Code,
                Name = c.Name,
                Batch = c.Batch,
                Schedule = c.Schedule,
                TuitionFee = c.TuitionFee,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                Status = c.Status,
                StudentCount = c.Students.Count,
                StaffCount = c.Staff.Count,
                TotalDiscountAmount = c.Students.Sum(s => s.DiscountAmount),
                ExpectedRevenue = (c.Students.Count * c.TuitionFee) - c.Students.Sum(s => s.DiscountAmount),
                TotalRevenue = c.Students.Sum(s => s.PaidAmount),
                TotalStaffExpense = c.Staff.Sum(st => st.CompensationRate),
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);

        return new PaginatedResult<CscaClassDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<CscaClassDetailDto>> GetClassByIdAsync(Guid id, CancellationToken ct)
    {
        var (companyId, _) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Course)
            .Include(c => c.Students)
            .Include(c => c.Staff)
                .ThenInclude(st => st.Employee)
            .Include(c => c.Schedules)
                .ThenInclude(schedule => schedule.Classroom)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted && c.CompanyId == companyId, ct);

        if (cls == null)
        {
            return Result<CscaClassDetailDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        var totalStudents = cls.Students.Count;
        var paidStudents = cls.Students.Count(s => s.PaymentStatus == PaymentStatus.Paid);
        var actualRevenue = cls.Students.Sum(s => s.PaidAmount);
        var expectedRevenue = cls.Students.Sum(s => GetPayableTuition(cls.TuitionFee, s.DiscountAmount));
        var totalStaffExpense = cls.Staff.Sum(st => st.CompensationRate);

        var detail = new CscaClassDetailDto
        {
            Id = cls.Id,
            CourseId = cls.CourseId,
            CourseTitle = cls.Course?.Title ?? string.Empty,
            Code = cls.Code,
            Name = cls.Name,
            Batch = cls.Batch,
            Schedule = cls.Schedule,
            TuitionFee = cls.TuitionFee,
            StartDate = cls.StartDate,
            EndDate = cls.EndDate,
            Status = cls.Status,
            CreatedAt = cls.CreatedAt,
            Students = cls.Students.Select(s => new CscaStudentDto
            {
                Id = s.Id,
                ClassId = s.ClassId,
                StudentName = s.StudentName,
                Age = s.Age,
                Hometown = s.Hometown,
                Email = s.Email,
                PhoneNumber = s.PhoneNumber,
                DiscountAmount = s.DiscountAmount,
                DiscountNote = s.DiscountNote,
                PayableAmount = GetPayableTuition(cls.TuitionFee, s.DiscountAmount),
                PaidAmount = s.PaidAmount,
                PaymentStatus = s.PaymentStatus,
                DebtDueDate = s.DebtDueDate,
                JoinedAt = s.JoinedAt,
                Notes = s.Notes
            }).OrderBy(s => s.JoinedAt).ToList(),
            Staff = cls.Staff.Select(st => new CscaStaffDto
            {
                Id = st.Id,
                ClassId = st.ClassId,
                EmployeeId = st.EmployeeId,
                EmployeeName = st.Employee != null ? st.Employee.FullName : string.Empty,
                EmployeeCode = st.Employee?.EmployeeCode,
                RoleInClass = st.RoleInClass,
                CompensationRate = st.CompensationRate,
                Notes = st.Notes
            }).ToList(),
            Schedules = cls.Schedules.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
                .Select(ToScheduleDto).ToList(),
            FinancialSummary = new ClassFinancialSummaryDto
            {
                ClassId = cls.Id,
                ClassCode = cls.Code,
                ClassName = cls.Name,
                TotalStudents = totalStudents,
                PaidStudents = paidStudents,
                ExpectedRevenue = expectedRevenue,
                ActualRevenue = actualRevenue,
                TotalStaffExpense = totalStaffExpense
            }
        };

        return Result<CscaClassDetailDto>.Success(detail);
    }

    public async Task<Result<CscaClassDto>> CreateClassAsync(CreateCscaClassRequest request, CancellationToken ct)
    {
        var (companyId, buId) = await GetContextAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<CscaClassDto>.Failure("Mã lớp và tên lớp là bắt buộc.");
        }
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate.Value.Date < request.StartDate.Value.Date)
            return Result<CscaClassDto>.Failure("Ngày kết thúc lớp phải sau hoặc bằng ngày bắt đầu.");

        var codeUpper = request.Code.Trim().ToUpper();
        var exists = await _db.CscaClasses.AnyAsync(c => c.CompanyId == companyId && c.Code == codeUpper && !c.IsDeleted, ct);
        if (exists)
        {
            return Result<CscaClassDto>.Failure($"Mã lớp '{codeUpper}' đã tồn tại trong hệ thống.");
        }

        var courseResult = await ResolveCourseAsync(request.CourseId, companyId, buId, ct);
        if (!courseResult.Succeeded)
            return Result<CscaClassDto>.Failure(courseResult.Errors.FirstOrDefault() ?? "Mỗi lớp học phải thuộc một khóa học.");

        var cls = new CscaClass
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            CourseId = courseResult.Value!.Id,
            Code = codeUpper,
            Name = request.Name.Trim(),
            Batch = request.Batch?.Trim() ?? string.Empty,
            Schedule = request.Schedule?.Trim() ?? string.Empty,
            TuitionFee = request.TuitionFee,
            StartDate = NormalizeClassDate(request.StartDate),
            EndDate = NormalizeClassDate(request.EndDate),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System"
        };

        _db.CscaClasses.Add(cls);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tạo lớp CSCA thành công: {ClassCode} ({ClassName})", cls.Code, cls.Name);

        var dto = new CscaClassDto
        {
            Id = cls.Id,
            CourseId = cls.CourseId,
            CourseTitle = courseResult.Value.Title,
            Code = cls.Code,
            Name = cls.Name,
            Batch = cls.Batch,
            Schedule = cls.Schedule,
            TuitionFee = cls.TuitionFee,
            StartDate = cls.StartDate,
            EndDate = cls.EndDate,
            Status = cls.Status,
            StudentCount = 0,
            StaffCount = 0,
            ExpectedRevenue = 0,
            TotalDiscountAmount = 0,
            TotalRevenue = 0,
            TotalStaffExpense = 0,
            CreatedAt = cls.CreatedAt
        };

        return Result<CscaClassDto>.Success(dto);
    }

    public async Task<Result<CscaClassDto>> UpdateClassAsync(Guid id, UpdateCscaClassRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(id, ct))
            return Result<CscaClassDto>.Failure("Không tìm thấy lớp học CSCA cần cập nhật.");
        var cls = await _db.CscaClasses
            .AsSplitQuery()
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

        if (cls == null)
        {
            return Result<CscaClassDto>.Failure("Không tìm thấy lớp học CSCA cần cập nhật.");
        }

        if (request.TuitionFee < 0)
            return Result<CscaClassDto>.Failure("Học phí phải là số không âm.");
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate.Value.Date < request.StartDate.Value.Date)
            return Result<CscaClassDto>.Failure("Ngày kết thúc lớp phải sau hoặc bằng ngày bắt đầu.");
        if (cls.Students.Any(student => student.DiscountAmount > request.TuitionFee))
            return Result<CscaClassDto>.Failure("Học phí mới không được thấp hơn mức giảm giá đã áp dụng cho học viên.");

        if (request.CourseId.HasValue && request.CourseId.Value != cls.CourseId)
        {
            var courseResult = await ResolveCourseAsync(request.CourseId, cls.CompanyId, cls.BusinessUnitId, ct);
            if (!courseResult.Succeeded)
                return Result<CscaClassDto>.Failure(courseResult.Errors.FirstOrDefault() ?? "Khóa học không hợp lệ.");
            cls.CourseId = courseResult.Value!.Id;
        }

        cls.Name = request.Name.Trim();
        cls.Batch = request.Batch?.Trim() ?? string.Empty;
        // The structured weekly schedule is the source of truth as soon as the class has lesson slots.
        // Keep the legacy text only for existing classes that have not yet been migrated to detailed slots.
        cls.Schedule = cls.Schedules.Count == 0
            ? request.Schedule?.Trim() ?? string.Empty
            : FormatScheduleSummary(cls.Schedules);
        cls.TuitionFee = request.TuitionFee;
        cls.StartDate = NormalizeClassDate(request.StartDate);
        cls.EndDate = NormalizeClassDate(request.EndDate);
        cls.Status = request.Status;
        cls.UpdatedAt = DateTime.UtcNow;
        cls.UpdatedBy = _currentUser.Username ?? "System";

        await _db.SaveChangesAsync(ct);

        var dto = new CscaClassDto
        {
            Id = cls.Id,
            CourseId = cls.CourseId,
            CourseTitle = await _db.Courses.Where(c => c.Id == cls.CourseId).Select(c => c.Title).FirstOrDefaultAsync(ct) ?? string.Empty,
            Code = cls.Code,
            Name = cls.Name,
            Batch = cls.Batch,
            Schedule = cls.Schedule,
            TuitionFee = cls.TuitionFee,
            StartDate = cls.StartDate,
            EndDate = cls.EndDate,
            Status = cls.Status,
            StudentCount = cls.Students.Count,
            StaffCount = cls.Staff.Count,
            TotalDiscountAmount = cls.Students.Sum(s => s.DiscountAmount),
            ExpectedRevenue = cls.Students.Sum(s => GetPayableTuition(cls.TuitionFee, s.DiscountAmount)),
            TotalRevenue = cls.Students.Sum(s => s.PaidAmount),
            TotalStaffExpense = cls.Staff.Sum(st => st.CompensationRate),
            CreatedAt = cls.CreatedAt
        };

        return Result<CscaClassDto>.Success(dto);
    }

    private static DateTime? NormalizeClassDate(DateTime? value)
        => value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc) : null;

    public async Task<Result<bool>> DeleteClassAsync(Guid id, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(id, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA cần xóa.");
        var cls = await _db.CscaClasses.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
        if (cls == null)
        {
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA cần xóa.");
        }

        cls.IsDeleted = true;
        cls.DeletedAt = DateTime.UtcNow;
        cls.DeletedBy = _currentUser.Username ?? "System";

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

}
