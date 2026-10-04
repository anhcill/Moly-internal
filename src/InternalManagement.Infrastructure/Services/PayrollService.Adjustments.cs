using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class PayrollService
{
    public async Task<Result<List<PayrollAdjustmentDto>>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<List<PayrollAdjustmentDto>>.Failure("Không tìm thấy kỳ lương.");
        }

        var adjustmentEntities = await _db.PayrollAdjustments
            .AsNoTracking()
            .Where(a => a.PayrollPeriodId == periodId &&
                        (!period.BusinessUnitId.HasValue || a.Employee.BusinessUnitId == period.BusinessUnitId))
            .Include(a => a.Employee)
            .OrderBy(a => a.Employee.EmployeeCode)
            .ToListAsync(ct);
        var adjustments = adjustmentEntities.Select(MapAdjustment).ToList();

        return Result<List<PayrollAdjustmentDto>>.Success(adjustments);
    }

    public async Task<Result<PayrollAdjustmentDto>> AddAdjustmentAsync(Guid periodId, CreatePayrollAdjustmentRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<PayrollAdjustmentDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status >= PayrollStatus.Reviewing)
        {
            return Result<PayrollAdjustmentDto>.Failure(
                "Kỳ lương đã gửi duyệt hoặc khóa, không thể chỉnh sửa khoản lương.");
        }

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.CompanyId == companyId, ct);
        if (emp == null)
        {
            return Result<PayrollAdjustmentDto>.Failure("Không tìm thấy nhân viên.");
        }

        if (period.BusinessUnitId.HasValue && emp.BusinessUnitId != period.BusinessUnitId)
        {
            return Result<PayrollAdjustmentDto>.Failure(
                "Không thể thêm khoản lương: nhân viên không thuộc đơn vị kinh doanh của kỳ lương.");
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return Result<PayrollAdjustmentDto>.Failure("Loại khoản lương không được để trống.");
        }

        if (request.Amount < 0)
        {
            return Result<PayrollAdjustmentDto>.Failure("Số tiền khoản lương không được âm.");
        }

        var adj = new PayrollAdjustment
        {
            PayrollPeriodId = periodId,
            EmployeeId = request.EmployeeId,
            Type = NormalizeComponentCode(request.Type),
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            CreatedBy = _currentUser.Username ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.PayrollAdjustments.Add(adj);
        await _db.SaveChangesAsync(ct);

        var dto = MapAdjustment(adj, emp.EmployeeCode, emp.FullName);

        return Result<PayrollAdjustmentDto>.Success(dto);
    }

    public async Task<Result<bool>> DeleteAdjustmentAsync(Guid adjustmentId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var adj = await _db.PayrollAdjustments
            .Include(a => a.PayrollPeriod)
            .FirstOrDefaultAsync(
            a => a.Id == adjustmentId && a.PayrollPeriod.CompanyId == companyId, ct);
        if (adj == null)
        {
            return Result<bool>.Failure("Không tìm thấy khoản điều chỉnh.");
        }

        if (adj.PayrollPeriod.Status >= PayrollStatus.Reviewing)
        {
            return Result<bool>.Failure(
                "Kỳ lương đã gửi duyệt hoặc khóa, không thể chỉnh sửa khoản lương.");
        }

        _db.PayrollAdjustments.Remove(adj);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

}
