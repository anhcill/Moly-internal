using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class FinanceWorkspaceService
{
    public async Task<Result<FinanceWorkspaceReportDto>> GetWorkspaceAsync(
        DateTime? from, int horizonMonths, decimal collectionPercent, decimal costFactor,
        decimal openingCash, CancellationToken ct)
    {
        if (horizonMonths is < 1 or > 24 || collectionPercent is < 0 or > 100 || costFactor is < 0 or > 10)
            return Result<FinanceWorkspaceReportDto>.Failure("Số tháng phải từ 1–24, tỷ lệ thu từ 0–100%, hệ số chi phí từ 0–10.");
        var company = await CompanyAsync(ct);
        if (company is null)
            return Result<FinanceWorkspaceReportDto>.Failure("Không xác định được công ty hiện tại.");

        var now = DateTime.UtcNow;
        var localStart = from ?? now.AddHours(7);
        if (localStart.Year is < 2000 or > 2100)
            return Result<FinanceWorkspaceReportDto>.Failure("Năm báo cáo phải từ 2000 đến 2100.");
        var start = new DateTime(localStart.Year, localStart.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);
        var end = start.AddMonths(horizonMonths);
        var forecastStart = now > start ? now : start;
        var queryEnd = now > end ? now : end;
        var unit = currentUser?.BusinessUnitId;
        var transactions = await db.FinanceTransactions.AsNoTracking()
            .Where(x => x.CompanyId == company.Value && (!unit.HasValue || x.BusinessUnitId == unit)
                && x.TransactionDate >= start && x.TransactionDate < queryEnd)
            .ToListAsync(ct);
        var plans = await db.PlannedCashMovements.AsNoTracking()
            .Where(x => x.CompanyId == company.Value && (!unit.HasValue || x.BusinessUnitId == unit)
                && x.PlannedDate < end)
            .OrderBy(x => x.PlannedDate).ToListAsync(ct);
        var students = await GetReceivablesForCompany(company.Value, ct);
        var teachers = await GetTeacherCostsForCompany(company.Value, ct);
        var snapshots = await db.CscaTeacherCostSnapshots.AsNoTracking()
            .Where(x => x.CompanyId == company.Value && (!unit.HasValue || x.BusinessUnitId == unit || x.BusinessUnitId == null)
                && x.SourceSystem == "CSCA_COURSE" && x.IsCurrent)
            .ToListAsync(ct);
        var warnings = new List<string>
        {
            "Số dư được tính từ tiền đầu kỳ bạn nhập và sổ thu chi; chưa phải số dư ngân hàng đã đối soát.",
            "Công nợ và lương chưa trả là số hiện tại. Nợ quá hạn/chưa hẹn ngày được giả định thu ở kỳ hiện tại; lương đã phát sinh được giả định trả ngay, lịch dạy tương lai được trả trong tháng dạy.",
            "Kế hoạch thu chi là khoản bổ sung. Không nhập lại học phí còn nợ hoặc lương đã có trong dữ liệu để tránh dự báo trùng.",
            "Hệ số chi phí chỉ điều chỉnh kế hoạch chi và buổi dạy chưa phát sinh; không thay đổi số lương đã làm còn phải trả."
        };
        if (start > now)
            warnings.Add("Ngày bắt đầu ở tương lai: tiền đầu kỳ là giả định tại ngày bắt đầu, không phải số dư hiện tại.");
        if (snapshots.Count == 0)
            warnings.Add("Chưa có dữ liệu lương CSCA Course; dự báo đang thiếu chi phí giáo viên.");
        else if (unit.HasValue && snapshots.Any(x => x.BusinessUnitId == null))
            warnings.Add("Một phần chi phí lương CSCA chưa gắn Business Unit; đang hiển thị như chi phí dùng chung để không làm mất dữ liệu.");
        else if (snapshots.Max(x => x.SyncedAt) < now.AddHours(-24))
            warnings.Add("Dữ liệu lương đã hơn 24 giờ chưa đồng bộ. Hãy đồng bộ trước khi chốt ngân sách.");
        if (snapshots.Any(x => !x.EmployeeId.HasValue))
            warnings.Add("Có giáo viên LMS chưa liên kết hồ sơ nhân sự: chi phí vẫn được dự báo riêng, chưa thể ứng/trả tiền tại đây.");
        if (snapshots.Any(x => x.BonusAmount != 0 || x.DeductionAmount != 0))
            warnings.Add("Thưởng/khấu trừ kỳ lương cần đối soát với CSCA Course trước khi chi trả; dự báo này dùng chi phí giảng dạy theo buổi.");
        if (transactions.Any(x => x.TransactionDate > now))
            warnings.Add("Có giao dịch thực tế mang ngày tương lai; các giao dịch này chưa được tính là tiền đã thu/chi.");

        var expected = new List<WorkspaceExpectedCash>();
        foreach (var student in students.Where(x => x.OutstandingAmount > 0))
        {
            var date = student.DueDate ?? forecastStart;
            if (date < forecastStart) date = forecastStart;
            expected.Add(new(date, "Student", student.OutstandingAmount * collectionPercent / 100, 0));
        }
        foreach (var plan in plans.Where(x => x.Status == "Planned"))
        {
            var date = plan.PlannedDate < forecastStart ? forecastStart : plan.PlannedDate;
            expected.Add(new(date, "Plan", plan.TransactionType == TransactionType.Income ? plan.Amount : 0,
                plan.TransactionType == TransactionType.Expense ? plan.Amount * costFactor : 0));
        }
        foreach (var teacher in teachers)
        {
            var advanceCredit = teacher.RemainingAdvance;
            AddTeacherCash(forecastStart, teacher.UnpaidEarned, ref advanceCredit);
            foreach (var session in snapshots.Where(x => x.EmployeeId == teacher.EmployeeId && !x.IsCompleted
                         && !IsSettledLmsSession(x.PaymentStatus) && !IsCancelledSession(x.Status))
                         .OrderBy(x => x.EndTime))
            {
                var projected = Math.Max(0, (session.ProjectedAmount ?? session.AccruedAmount) - session.AccruedAmount);
                AddTeacherCash(session.EndTime < forecastStart ? forecastStart : session.EndTime, projected, ref advanceCredit, estimated: true);
            }
        }
        foreach (var group in snapshots.Where(x => !x.EmployeeId.HasValue && !IsSettledLmsSession(x.PaymentStatus)
                     && !IsCancelledSession(x.Status)).GroupBy(x => x.TeacherLmsId))
        {
            decimal noAdvance = 0;
            AddTeacherCash(forecastStart, group.Sum(x => x.AccruedAmount), ref noAdvance);
            foreach (var session in group.Where(x => !x.IsCompleted))
                AddTeacherCash(session.EndTime < forecastStart ? forecastStart : session.EndTime,
                    Math.Max(0, (session.ProjectedAmount ?? session.AccruedAmount) - session.AccruedAmount), ref noAdvance, estimated: true);
        }

        var rows = new List<FinanceWorkspaceMonthDto>();
        var balance = openingCash;
        for (var i = 0; i < horizonMonths; i++)
        {
            var month = start.AddMonths(i);
            var next = month.AddMonths(1);
            var actual = transactions.Where(x => x.TransactionDate >= month && x.TransactionDate < next && x.TransactionDate <= now).ToList();
            var projected = expected.Where(x => x.Date >= month && x.Date < next && x.Date >= now).ToList();
            var actualIn = actual.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount);
            var actualOut = actual.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount);
            var forecastIn = projected.Sum(x => x.Inflow);
            var forecastOut = projected.Sum(x => x.Outflow);
            var inflow = actualIn + forecastIn;
            var outflow = actualOut + forecastOut;
            var kind = next <= now ? "Actual" : month <= now ? "Mixed" : "Forecast";
            rows.Add(new FinanceWorkspaceMonthDto(month.AddHours(7), kind, balance, inflow, outflow,
                projected.Where(x => x.Source == "Student").Sum(x => x.Inflow) + actual.Where(x => x.ReferenceType == "StudentReceipt").Sum(x => x.Amount),
                projected.Where(x => x.Source == "Teacher").Sum(x => x.Outflow) + actual.Where(x => x.ReferenceType == "TeacherPayment").Sum(x => x.Amount),
                projected.Where(x => x.Source == "Plan").Sum(x => x.Inflow), projected.Where(x => x.Source == "Plan").Sum(x => x.Outflow),
                balance + inflow - outflow)
            {
                ActualInflow = actualIn, ActualOutflow = actualOut,
                ForecastInflow = forecastIn, ForecastOutflow = forecastOut
            });
            balance += inflow - outflow;
        }
        var cash = openingCash + transactions.Where(x => x.TransactionDate <= now)
            .Sum(x => x.TransactionType == TransactionType.Income ? x.Amount : -x.Amount);
        if (rows.Any(x => x.ClosingCash < 0))
            warnings.Add("Có tháng thiếu tiền theo kịch bản. Kiểm tra lịch thu nợ, lịch trả giáo viên và kế hoạch chi.");
        var unmappedPayable = snapshots.Where(x => !x.EmployeeId.HasValue && !IsSettledLmsSession(x.PaymentStatus)
            && !IsCancelledSession(x.Status)).Sum(x => x.AccruedAmount);
        return Result<FinanceWorkspaceReportDto>.Success(new(start, end, cash,
            students.Sum(x => x.OutstandingAmount), teachers.Sum(x => x.UnpaidEarned) + unmappedPayable,
            balance, rows, students, teachers, plans.Select(ToDto).ToList(),
            transactions.Where(x => x.TransactionDate <= now).OrderByDescending(x => x.TransactionDate)
                .Select(x => new FinanceWorkspaceCashMovementDto(x.Id, x.TransactionDate, x.TransactionType.ToString(),
                    x.TransactionType == TransactionType.Income ? x.Amount : 0,
                    x.TransactionType == TransactionType.Expense ? x.Amount : 0,
                    x.Description, x.ReferenceType, x.ReferenceId)).ToList())
        {
            AsOf = now, Warnings = warnings, CurrentCashIsReconciled = false
        });

        void AddTeacherCash(DateTime date, decimal liability, ref decimal advanceCredit, bool estimated = false)
        {
            var amount = Math.Max(0, liability) * (estimated ? costFactor : 1);
            var offset = Math.Min(advanceCredit, amount);
            advanceCredit -= offset;
            expected.Add(new(date, "Teacher", 0, amount - offset));
        }
    }

    private static bool IsSettledLmsSession(string status) =>
        status.Equals("paid", StringComparison.OrdinalIgnoreCase) || status.Equals("settled", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancelledSession(string status) =>
        status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) || status.Equals("canceled", StringComparison.OrdinalIgnoreCase);

    private sealed record WorkspaceExpectedCash(DateTime Date, string Source, decimal Inflow, decimal Outflow);
}
