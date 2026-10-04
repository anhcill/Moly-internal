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
    public async Task<PaginatedResult<PayslipDto>> GetMyPayslipsAsync(int pageIndex, int pageSize, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return new PaginatedResult<PayslipDto>(Array.Empty<PayslipDto>(), 0, pageIndex, pageSize);
        }

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId.Value, ct);
        if (emp == null)
        {
            return new PaginatedResult<PayslipDto>(Array.Empty<PayslipDto>(), 0, pageIndex, pageSize);
        }

        var query = _db.Payslips
            .AsNoTracking()
            .Where(ps => ps.EmployeeId == emp.Id && ps.Status == PayrollStatus.Published && ps.PayrollPeriod.Status == PayrollStatus.Published)
            .Include(ps => ps.PayrollPeriod)
            .Include(ps => ps.Employee)
                .ThenInclude(e => e.Department)
            .OrderByDescending(ps => ps.PayrollPeriod.StartDate);

        var count = await query.CountAsync(ct);
        var payslipEntities = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = payslipEntities
            .Select(ps => MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee))
            .ToList();

        return new PaginatedResult<PayslipDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<PayslipDto>> GetPersonalPayslipAsync(Guid periodId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return Result<PayslipDto>.Failure("Chưa xác thực người dùng.");
        }

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId.Value, ct);
        if (emp == null)
        {
            return Result<PayslipDto>.Failure("Tài khoản của bạn chưa liên kết với hồ sơ nhân viên.");
        }

        var ps = await _db.Payslips
            .AsNoTracking()
            .Include(p => p.PayrollPeriod)
            .Include(p => p.Employee)
                .ThenInclude(e => e.Department)
            .FirstOrDefaultAsync(p => p.PayrollPeriodId == periodId && p.EmployeeId == emp.Id && p.Status == PayrollStatus.Published, ct);

        if (ps == null)
        {
            return Result<PayslipDto>.Failure("Không tìm thấy phiếu lương cá nhân của bạn hoặc phiếu lương chưa được phát hành.");
        }

        var dto = MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee);

        return Result<PayslipDto>.Success(dto);
    }

}
