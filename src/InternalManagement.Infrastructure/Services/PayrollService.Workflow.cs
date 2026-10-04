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
    public async Task<Result<PayrollPeriodDto>> SubmitForReviewAsync(Guid periodId, SubmitPayrollForReviewRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Calculated)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể gửi duyệt kỳ lương đang ở trạng thái {period.Status}. Kỳ lương phải được Tính Lương (Calculated) trước.");
        }

        period.Status = PayrollStatus.Reviewing;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Reviewing,
                Comments = request.Comments ?? "Gửi duyệt kỳ lương",
                ApprovedAt = DateTime.UtcNow
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Open, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) submitted for review by {User}",
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

    public async Task<Result<PayrollPeriodDto>> ApprovePayrollAsync(Guid periodId, ApprovePayrollRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Reviewing && period.Status != PayrollStatus.Calculated)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể phê duyệt kỳ lương đang ở trạng thái {period.Status}.");
        }

        period.Status = PayrollStatus.Approved;
        period.ApprovedAt = DateTime.UtcNow;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Approved,
                Comments = request.Comments ?? "Phê duyệt kỳ lương",
                ApprovedAt = DateTime.UtcNow
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Open, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) approved by {User}",
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

    public async Task<Result<PayrollPeriodDto>> MarkAsPaidAsync(Guid periodId, MarkPayrollPaidRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Approved)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể đánh dấu đã chi cho kỳ lương chưa được phê duyệt (Trạng thái hiện tại: {period.Status}).");
        }

        period.Status = PayrollStatus.Paid;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Paid,
                Comments = request.Comments ?? "Xác nhận đã giải ngân chi lương",
                ApprovedAt = DateTime.UtcNow
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Settled, ct);
        if (_financePostingService is not null && period.TotalNetAmount > 0)
        {
            await _financePostingService.PostAsync(new FinancePostingRequest(
                period.CompanyId,
                period.BusinessUnitId,
                TransactionType.Expense,
                period.TotalNetAmount,
                DateTime.UtcNow,
                nameof(PayrollPeriod),
                period.Id,
                $"Chi lương kỳ {period.Name}",
                period.BusinessDocumentId), ct);
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) marked as PAID by {User}",
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

    public async Task<Result<PayrollPeriodDto>> PublishPayrollAsync(Guid periodId, PublishPayrollRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Paid && period.Status != PayrollStatus.Approved)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể phát hành kỳ lương đang ở trạng thái {period.Status}. Kỳ lương phải được Approved hoặc Paid trước khi phát hành.");
        }

        period.Status = PayrollStatus.Published;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var publishedTime = DateTime.UtcNow;
        foreach (var ps in period.Payslips)
        {
            ps.Status = PayrollStatus.Published;
            ps.PublishedAt = publishedTime;
            ps.UpdatedAt = publishedTime;
            ps.UpdatedBy = _currentUser.Username ?? "system";
        }

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Published,
                Comments = request.Comments ?? "Phát hành phiếu lương cho toàn bộ nhân sự",
                ApprovedAt = publishedTime
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Settled, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) PUBLISHED by {User}, {Count} payslips released",
            period.Name, period.Id, _currentUser.Username, period.Payslips.Count);

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

    public async Task<Result<List<PayrollApprovalDto>>> GetApprovalsAsync(Guid periodId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<List<PayrollApprovalDto>>.Failure("Không tìm thấy kỳ lương.");
        }

        var approvals = await _db.PayrollApprovals
            .AsNoTracking()
            .Where(a => a.PayrollPeriodId == periodId)
            .Include(a => a.Approver)
            .OrderByDescending(a => a.ApprovedAt)
            .Select(a => new PayrollApprovalDto(
                a.Id,
                a.PayrollPeriodId,
                a.ApproverId,
                a.Approver != null ? a.Approver.FullName : "System",
                a.Status,
                a.Comments,
                a.ApprovedAt))
            .ToListAsync(ct);

        return Result<List<PayrollApprovalDto>>.Success(approvals);
    }

}
