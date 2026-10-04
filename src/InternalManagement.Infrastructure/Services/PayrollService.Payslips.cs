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
    public async Task<PaginatedResult<PayslipDto>> GetPayslipsAsync(
        Guid periodId, Guid? departmentId, string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var query = _db.Payslips
            .AsNoTracking()
            .Where(ps => ps.PayrollPeriodId == periodId &&
                         ps.PayrollPeriod.CompanyId == companyId &&
                         (!ps.PayrollPeriod.BusinessUnitId.HasValue ||
                          ps.Employee.BusinessUnitId == ps.PayrollPeriod.BusinessUnitId))
            .Include(ps => ps.PayrollPeriod)
            .Include(ps => ps.Employee)
                .ThenInclude(e => e.Department)
            .AsQueryable();

        if (departmentId.HasValue && departmentId.Value != Guid.Empty)
        {
            query = query.Where(ps => ps.Employee.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(ps => ps.Employee.EmployeeCode.ToLower().Contains(s) ||
                                      ps.Employee.FullName.ToLower().Contains(s));
        }

        var count = await query.CountAsync(ct);
        var payslipEntities = await query
            .OrderBy(ps => ps.Employee.EmployeeCode)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = payslipEntities
            .Select(ps => MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee))
            .ToList();

        return new PaginatedResult<PayslipDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<PayslipDto>> GetPayslipByIdAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var ps = await _db.Payslips
            .AsNoTracking()
            .Include(p => p.PayrollPeriod)
            .Include(p => p.Employee)
                .ThenInclude(e => e.Department)
            .FirstOrDefaultAsync(p => p.Id == id &&
                                      p.PayrollPeriod.CompanyId == companyId &&
                                      (!p.PayrollPeriod.BusinessUnitId.HasValue ||
                                       p.Employee.BusinessUnitId == p.PayrollPeriod.BusinessUnitId), ct);

        if (ps == null)
        {
            return Result<PayslipDto>.Failure("Không tìm thấy phiếu lương.");
        }

        var dto = MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee);

        return Result<PayslipDto>.Success(dto);
    }

}
