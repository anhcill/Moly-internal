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
    public async Task<PaginatedResult<PayrollPeriodDto>> GetPayrollPeriodsAsync(
        int? year, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var scopedBusinessUnitIds = await ResolveSegmentBusinessUnitIdsAsync(
            companyId, businessUnitId, businessSegment, ct);
        var query = _db.PayrollPeriods
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Include(p => p.Payslips)
            .AsQueryable();

        if (scopedBusinessUnitIds != null)
        {
            query = query.Where(p => p.BusinessUnitId.HasValue &&
                                     scopedBusinessUnitIds.Contains(p.BusinessUnitId.Value));
        }

        if (year.HasValue)
        {
            query = query.Where(p => p.StartDate.Year == year.Value || p.EndDate.Year == year.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayrollStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(p => p.Status == parsedStatus);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.StartDate)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PayrollPeriodDto(
                p.Id,
                p.CompanyId,
                p.BusinessUnitId,
                p.Name,
                p.StartDate,
                p.EndDate,
                p.Status,
                p.TotalGrossAmount,
                p.TotalNetAmount,
                p.Payslips.Count,
                p.CalculatedAt,
                p.ApprovedAt,
                p.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedResult<PayrollPeriodDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<PayrollPeriodDetailDto>> GetPayrollPeriodByIdAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .AsNoTracking()
            .Include(p => p.Payslips)
                .ThenInclude(ps => ps.Employee)
                    .ThenInclude(e => e.Department)
            .Include(p => p.Adjustments)
                .ThenInclude(a => a.Employee)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDetailDto>.Failure("Không tìm thấy kỳ lương.");
        }

        var payslips = period.Payslips
            .Where(ps => !period.BusinessUnitId.HasValue || ps.Employee.BusinessUnitId == period.BusinessUnitId)
            .Select(ps => MapPayslip(ps, period.Name, ps.Employee))
            .ToList();

        var adjustments = period.Adjustments
            .Where(a => !period.BusinessUnitId.HasValue || a.Employee.BusinessUnitId == period.BusinessUnitId)
            .Select(MapAdjustment)
            .ToList();

        var detail = new PayrollPeriodDetailDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt,
            payslips,
            adjustments);

        return Result<PayrollPeriodDetailDto>.Success(detail);
    }

    public async Task<Result<PayrollPeriodDto>> CreatePayrollPeriodAsync(CreatePayrollPeriodRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<PayrollPeriodDto>.Failure("Tên kỳ lương không được để trống.");
        }

        if (request.EndDate < request.StartDate)
        {
            return Result<PayrollPeriodDto>.Failure("Ngày kết thúc kỳ lương phải sau ngày bắt đầu.");
        }

        if (request.BusinessUnitId.HasValue)
        {
            var validBusinessUnit = await _db.BusinessUnits.AsNoTracking().AnyAsync(b =>
                b.Id == request.BusinessUnitId.Value && b.CompanyId == companyId && !b.IsDeleted && b.IsActive, ct);
            if (!validBusinessUnit)
            {
                return Result<PayrollPeriodDto>.Failure("Đơn vị kinh doanh của kỳ lương không hợp lệ.");
            }
        }

        var overlapsExistingPeriod = await _db.PayrollPeriods.AsNoTracking().AnyAsync(p =>
            p.CompanyId == companyId &&
            p.Status != PayrollStatus.Cancelled &&
            p.StartDate <= request.EndDate &&
            p.EndDate >= request.StartDate &&
            (request.BusinessUnitId.HasValue
                ? p.BusinessUnitId == request.BusinessUnitId.Value
                : p.BusinessUnitId == null), ct);
        if (overlapsExistingPeriod)
        {
            return Result<PayrollPeriodDto>.Failure(
                "Khoảng ngày của kỳ lương bị chồng lấn với một kỳ lương chưa hủy trong cùng mảng. Hãy dùng kỳ hiện có hoặc chọn khoảng ngày khác.");
        }

        var period = new PayrollPeriod
        {
            CompanyId = companyId,
            BusinessUnitId = request.BusinessUnitId,
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = PayrollStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "system"
        };

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Draft, ct);
        _db.PayrollPeriods.Add(period);
        await _db.SaveChangesAsync(ct);

        var dto = new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            0,
            0,
            0,
            null,
            null,
            period.CreatedAt);

        return Result<PayrollPeriodDto>.Success(dto);
    }

    public async Task<Result<PayrollPeriodDto>> CancelPayrollPeriodAsync(
        Guid periodId,
        CancelPayrollPeriodRequest request,
        CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương cần hủy.");
        }

        if (period.Status != PayrollStatus.Draft)
        {
            return Result<PayrollPeriodDto>.Failure(
                "Chỉ được hủy kỳ lương đang ở Bản nháp. Kỳ đã tính hoặc đã gửi duyệt phải giữ nguyên để bảo toàn dấu vết nghiệp vụ.");
        }

        var now = DateTime.UtcNow;
        period.Status = PayrollStatus.Cancelled;
        period.UpdatedAt = now;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var actorId = _currentUser.UserId;
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            CompanyId = period.CompanyId,
            BusinessUnitId = period.BusinessUnitId,
            Action = "Cancel",
            EntityName = nameof(PayrollPeriod),
            EntityId = period.Id.ToString(),
            OldValues = "{\"Status\":\"Draft\"}",
            NewValues = $"{{\"Status\":\"Cancelled\",\"Reason\":{System.Text.Json.JsonSerializer.Serialize(request.Comments?.Trim() ?? "Hủy kỳ lương nháp")}}}",
            CreatedAt = now
        });

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Voided, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) was cancelled by {User}",
            period.Name, period.Id, _currentUser.Username);

        return Result<PayrollPeriodDto>.Success(new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.Payslips.Count,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt));
    }

}
