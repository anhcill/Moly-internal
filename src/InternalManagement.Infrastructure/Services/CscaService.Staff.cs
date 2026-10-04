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
    public async Task<Result<CscaStaffDto>> AssignStaffAsync(Guid classId, AssignStaffRequest request, CancellationToken ct)
    {
        var validation = ValidateStaffAssignment(request.RoleInClass, request.CompensationRate);
        if (validation != null)
            return Result<CscaStaffDto>.Failure(validation);

        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStaffDto>.Failure("Không tìm thấy lớp học CSCA.");
        var cls = await _db.CscaClasses
            .Include(c => c.Staff)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted, ct);

        if (cls == null)
        {
            return Result<CscaStaffDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId && !e.IsDeleted, ct);
        if (employee == null)
        {
            return Result<CscaStaffDto>.Failure("Không tìm thấy nhân viên/giảng viên được chọn.");
        }

        var existingStaff = cls.Staff.FirstOrDefault(s => s.EmployeeId == request.EmployeeId);
        if (existingStaff != null)
        {
            existingStaff.RoleInClass = request.RoleInClass.Trim();
            existingStaff.CompensationRate = request.CompensationRate;
            existingStaff.Notes = request.Notes?.Trim();
            existingStaff.UpdatedAt = DateTime.UtcNow;
            existingStaff.UpdatedBy = _currentUser.Username ?? "System";
        }
        else
        {
            existingStaff = new CscaClassStaff
            {
                ClassId = classId,
                EmployeeId = request.EmployeeId,
                RoleInClass = request.RoleInClass.Trim(),
                CompensationRate = request.CompensationRate,
                Notes = request.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username ?? "System"
            };
            cls.Staff.Add(existingStaff);
            _db.CscaClassStaffs.Add(existingStaff);
        }

        await SyncClassProfitAllocationAsync(cls, ct);
        await _db.SaveChangesAsync(ct);

        var dto = new CscaStaffDto
        {
            Id = existingStaff.Id,
            ClassId = classId,
            EmployeeId = employee.Id,
            EmployeeName = employee.FullName,
            EmployeeCode = employee.EmployeeCode,
            RoleInClass = existingStaff.RoleInClass,
            CompensationRate = existingStaff.CompensationRate,
            Notes = existingStaff.Notes
        };

        return Result<CscaStaffDto>.Success(dto);
    }

    public async Task<Result<CscaStaffDto>> UpdateStaffAsync(
        Guid classId, Guid staffId, UpdateCscaStaffRequest request, CancellationToken ct)
    {
        var validation = ValidateStaffAssignment(request.RoleInClass, request.CompensationRate);
        if (validation != null)
            return Result<CscaStaffDto>.Failure(validation);

        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStaffDto>.Failure("Không tìm thấy lớp học CSCA.");

        var staff = await _db.CscaClassStaffs
            .Include(item => item.Class)
                .ThenInclude(cls => cls.Staff)
            .Include(item => item.Employee)
            .FirstOrDefaultAsync(item => item.Id == staffId && item.ClassId == classId, ct);
        if (staff is null || staff.Employee is null)
            return Result<CscaStaffDto>.Failure("Không tìm thấy phân công nhân sự trong lớp.");

        staff.RoleInClass = request.RoleInClass.Trim();
        staff.CompensationRate = request.CompensationRate;
        staff.Notes = request.Notes?.Trim();
        staff.UpdatedAt = DateTime.UtcNow;
        staff.UpdatedBy = _currentUser.Username ?? "System";

        if (staff.Class is not null)
            await SyncClassProfitAllocationAsync(staff.Class, ct);

        await _db.SaveChangesAsync(ct);
        return Result<CscaStaffDto>.Success(new CscaStaffDto
        {
            Id = staff.Id,
            ClassId = staff.ClassId,
            EmployeeId = staff.EmployeeId,
            EmployeeName = staff.Employee.FullName,
            EmployeeCode = staff.Employee.EmployeeCode,
            RoleInClass = staff.RoleInClass,
            CompensationRate = staff.CompensationRate,
            Notes = staff.Notes
        });
    }

    public async Task<Result<bool>> RemoveStaffAsync(Guid classId, Guid staffId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA.");
        var staff = await _db.CscaClassStaffs
            .Include(s => s.Class)
                .ThenInclude(c => c.Staff)
            .FirstOrDefaultAsync(s => s.Id == staffId && s.ClassId == classId, ct);

        if (staff == null)
        {
            return Result<bool>.Failure("Không tìm thấy phân công nhân sự trong lớp.");
        }

        var cls = staff.Class;
        _db.CscaClassStaffs.Remove(staff);

        if (cls != null)
        {
            cls.Staff.Remove(staff);
            await SyncClassProfitAllocationAsync(cls, ct);
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ClassFinancialSummaryDto>> GetClassFinancialSummaryAsync(Guid classId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses
            .AsNoTracking()
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted && c.CompanyId == companyId &&
                (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);

        if (cls == null)
        {
            return Result<ClassFinancialSummaryDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        var totalStudents = cls.Students.Count;
        var paidStudents = cls.Students.Count(s => s.PaymentStatus == PaymentStatus.Paid);
        var actualRevenue = cls.Students.Sum(s => s.PaidAmount);
        var expectedRevenue = cls.Students.Sum(s => GetPayableTuition(cls.TuitionFee, s.DiscountAmount));
        var totalStaffExpense = cls.Staff.Sum(st => st.CompensationRate);

        var summary = new ClassFinancialSummaryDto
        {
            ClassId = cls.Id,
            ClassCode = cls.Code,
            ClassName = cls.Name,
            TotalStudents = totalStudents,
            PaidStudents = paidStudents,
            ExpectedRevenue = expectedRevenue,
            ActualRevenue = actualRevenue,
            TotalStaffExpense = totalStaffExpense
        };

        return Result<ClassFinancialSummaryDto>.Success(summary);
    }

}
