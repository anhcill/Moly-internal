using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Finance;
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

        var paidAt = DateTime.UtcNow;
        period.Status = PayrollStatus.Paid;
        period.UpdatedAt = paidAt;
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
                ApprovedAt = paidAt
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
                paidAt,
                nameof(PayrollPeriod),
                period.Id,
                $"Chi lương kỳ {period.Name}",
                period.BusinessDocumentId), ct);
        }

        // Reconcile LMS teacher cost snapshots and open teacher advances covered by this payroll period
        // so Finance Workspace never pays the same sessions twice and LMS reflects "paid".
        var settledLmsTeachers = await SettleTeacherSnapshotsAndAdvancesForPeriodAsync(period, paidAt, ct);

        await _db.SaveChangesAsync(ct);

        if (_lmsClient is not null && settledLmsTeachers.Count > 0)
        {
            var periodLabel = $"{period.StartDate:yyyy-MM}";
            foreach (var teacher in settledLmsTeachers)
            {
                try
                {
                    var correlationId = Guid.NewGuid().ToString("N");
                    await _lmsClient.UpdateTeacherSettlementAsync(
                        new LmsTeacherSettlementUpdate(
                            teacher.TeacherLmsId,
                            teacher.BonusAmount,
                            teacher.DeductionAmount,
                            "paid",
                            $"Đã chi qua kỳ lương {period.Name}",
                            paidAt,
                            periodLabel),
                        new LmsOutboundRequestContext($"payroll-paid:{period.Id:N}:{teacher.EmployeeId:N}", correlationId),
                        ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Payroll period {PeriodId} paid locally, but failed to push 'paid' status to LMS for teacher {EmployeeId} (LMS #{TeacherLmsId})",
                        period.Id, teacher.EmployeeId, teacher.TeacherLmsId);
                }
            }
        }

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

    private sealed record SettledLmsTeacher(Guid EmployeeId, long TeacherLmsId, decimal BonusAmount, decimal DeductionAmount);

    private async Task<List<SettledLmsTeacher>> SettleTeacherSnapshotsAndAdvancesForPeriodAsync(
        PayrollPeriod period,
        DateTime paidAt,
        CancellationToken ct)
    {
        var result = new List<SettledLmsTeacher>();
        var paidEmployeeIds = period.Payslips
            .Select(ps => ps.EmployeeId)
            .Distinct()
            .ToList();
        if (paidEmployeeIds.Count == 0)
            return result;

        var periodStartUtc = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEndExclusiveUtc = period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var snapshotsQuery = _db.CscaTeacherCostSnapshots
            .Where(x => x.CompanyId == period.CompanyId &&
                x.SourceSystem == "CSCA_COURSE" &&
                x.IsCurrent &&
                x.IsCompleted &&
                x.EmployeeId != null &&
                paidEmployeeIds.Contains(x.EmployeeId.Value) &&
                x.StartTime >= periodStartUtc &&
                x.StartTime < periodEndExclusiveUtc);
        if (period.BusinessUnitId.HasValue)
        {
            var buId = period.BusinessUnitId.Value;
            snapshotsQuery = snapshotsQuery.Where(x => x.BusinessUnitId == buId || x.BusinessUnitId == null);
        }

        var snapshots = (await snapshotsQuery.ToListAsync(ct))
            .Where(x => !IsCancelledStatus(x.Status) && !IsSnapshotPaid(x))
            .ToList();

        var snapshotIds = snapshots.Select(x => x.Id).ToList();
        var existingAllocations = snapshotIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.TeacherPaymentAllocations.AsNoTracking()
                .Where(a => a.CompanyId == period.CompanyId && snapshotIds.Contains(a.TeacherCostSnapshotId))
                .GroupBy(a => a.TeacherCostSnapshotId)
                .Select(g => new { SnapshotId = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.SnapshotId, x => x.Amount, ct);

        foreach (var group in snapshots.GroupBy(x => x.EmployeeId!.Value))
        {
            var empSnapshots = group.OrderBy(x => x.StartTime).ThenBy(x => x.Id).ToList();
            var unpaidPairs = empSnapshots
                .Select(s => new { Snapshot = s, Remaining = Math.Max(0m, s.AccruedAmount - existingAllocations.GetValueOrDefault(s.Id)) })
                .Where(x => x.Remaining > 0m)
                .ToList();
            if (unpaidPairs.Count == 0)
                continue;

            // Create a non-cash reconciliation TeacherPayment (cash was posted once on PayrollPeriod)
            // so Finance Workspace sees these snapshots as 100% settled and cannot double-pay them.
            var totalAllocated = unpaidPairs.Sum(x => x.Remaining);
            var reconciliationPayment = new TeacherPayment
            {
                CompanyId = period.CompanyId,
                BusinessUnitId = period.BusinessUnitId ?? empSnapshots.First().BusinessUnitId,
                EmployeeId = group.Key,
                Amount = 0m,
                AdvanceAppliedAmount = totalAllocated,
                PaidAt = paidAt,
                Description = $"Đối soát chi qua kỳ lương {period.Name}",
                IdempotencyKey = $"payroll:{period.Id:N}:{group.Key:N}"
            };
            _db.TeacherPayments.Add(reconciliationPayment);

            foreach (var item in unpaidPairs)
            {
                _db.TeacherPaymentAllocations.Add(new TeacherPaymentAllocation
                {
                    CompanyId = period.CompanyId,
                    BusinessUnitId = item.Snapshot.BusinessUnitId,
                    TeacherPaymentId = reconciliationPayment.Id,
                    TeacherCostSnapshotId = item.Snapshot.Id,
                    Amount = item.Remaining
                });
                item.Snapshot.PaymentStatus = "paid";
                item.Snapshot.PaidAt = paidAt;
                item.Snapshot.LocalSettlementVersion++;
            }

            var latest = empSnapshots
                .OrderByDescending(x => x.SourceUpdatedAt ?? x.SourceCalculatedAt)
                .First();
            if (latest.TeacherLmsId > 0)
            {
                result.Add(new SettledLmsTeacher(group.Key, latest.TeacherLmsId, latest.BonusAmount, latest.DeductionAmount));
            }
        }

        // Apply open TeacherAdvances that were deducted in this period's payslips.
        var payslipsByEmp = period.Payslips.ToDictionary(ps => ps.EmployeeId);
        var openAdvances = (await _db.TeacherAdvances
            .Where(a => a.CompanyId == period.CompanyId &&
                a.Status != "Cancelled" &&
                paidEmployeeIds.Contains(a.EmployeeId))
            .OrderBy(a => a.IssuedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct))
            .Where(a => a.Amount > a.ReturnedAmount + a.AppliedAmount)
            .GroupBy(a => a.EmployeeId);

        foreach (var group in openAdvances)
        {
            if (!payslipsByEmp.TryGetValue(group.Key, out var payslip))
                continue;
            // Only apply advances for employees who actually had deductions/income in this payslip.
            var availableDeduction = Math.Max(0m, payslip.TotalDeductions - payslip.HealthInsurance);
            if (availableDeduction <= 0m)
                continue;

            var toApply = availableDeduction;
            foreach (var advance in group)
            {
                if (toApply <= 0m) break;
                var remaining = advance.Amount - advance.ReturnedAmount - advance.AppliedAmount;
                var applied = Math.Min(toApply, remaining);
                if (applied <= 0m) continue;
                advance.AppliedAmount += applied;
                advance.Status = advance.Amount <= advance.ReturnedAmount + advance.AppliedAmount ? "Settled" : "Open";
                toApply -= applied;
            }
        }

        return result;
    }

}
