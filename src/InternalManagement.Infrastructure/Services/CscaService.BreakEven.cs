using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService
{
    public async Task<Result<ClassBreakEvenAnalysisDto>> GetClassBreakEvenAnalysisAsync(
        Guid classId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses
            .AsNoTracking()
            .Include(x => x.Students)
            .FirstOrDefaultAsync(x => x.Id == classId && !x.IsDeleted && x.CompanyId == companyId &&
                (!businessUnitId.HasValue || x.BusinessUnitId == businessUnitId), ct);
        if (cls is null)
            return Result<ClassBreakEvenAnalysisDto>.Failure("Không tìm thấy lớp học CSCA.");

        var rangeFrom = from ?? cls.StartDate ?? cls.CreatedAt;
        var rangeTo = to ?? cls.EndDate?.Date.AddDays(1) ?? DateTime.UtcNow;
        if (rangeTo <= rangeFrom)
            return Result<ClassBreakEvenAnalysisDto>.Failure("Khoảng thời gian hòa vốn không hợp lệ.");
        if (rangeTo - rangeFrom > TimeSpan.FromDays(366))
            return Result<ClassBreakEvenAnalysisDto>.Failure("Khoảng thời gian hòa vốn không được vượt quá 366 ngày.");

        var teacherCost = await GetClassTeacherCostAsync(cls, ct, rangeFrom, rangeTo);
        decimal fixedCost = 0;
        decimal variableCost = 0;
        decimal unclassifiedExpense = 0;
        var warnings = new List<string>();

        if (_integrationDb is null)
        {
            warnings.Add("Chưa có Finance ledger context; chi phí cố định/biến đổi từ sổ giao dịch chưa được tính.");
        }
        else
        {
            var transactions = await _integrationDb.FinanceTransactions
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.CompanyId == companyId && x.TransactionType == TransactionType.Expense &&
                            x.TransactionDate >= rangeFrom && x.TransactionDate < rangeTo &&
                            (x.ReferenceId == classId ||
                             (cls.BusinessUnitId.HasValue && x.BusinessUnitId == cls.BusinessUnitId &&
                              x.ReferenceId == null)))
                .ToListAsync(ct);

            var directTransactions = transactions.Where(x => x.ReferenceId == classId);
            foreach (var transaction in directTransactions)
            {
                // Payroll transactions may already include the same teacher cost
                // represented by the CSCA Course snapshot, so do not double count.
                if (IsPayrollReference(transaction))
                    continue;

                switch (ClassifyLedgerCost(transaction))
                {
                    case LedgerCostKind.Fixed:
                        fixedCost += transaction.Amount;
                        break;
                    case LedgerCostKind.Variable:
                        variableCost += transaction.Amount;
                        break;
                    default:
                        unclassifiedExpense += transaction.Amount;
                        break;
                }
            }

            if (unclassifiedExpense > 0)
                warnings.Add("Có chi phí trong Finance ledger chưa được phân loại cố định/biến đổi; chưa đưa vào công thức hòa vốn.");

            var unallocatedSharedFixedCost = transactions
                .Where(x => x.ReferenceId == null && !IsPayrollReference(x))
                .Where(x => ClassifyLedgerCost(x) == LedgerCostKind.Fixed)
                .Sum(x => x.Amount);
            if (unallocatedSharedFixedCost > 0)
                warnings.Add($"Có {unallocatedSharedFixedCost:N0} đ chi phí cố định dùng chung của đơn vị chưa được phân bổ cho lớp; chưa đưa vào công thức hòa vốn.");
        }

        var activeStudents = cls.Students.Where(x => x.PaymentStatus is not PaymentStatus.Cancelled and not PaymentStatus.Refunded).ToList();
        var enrolledStudents = activeStudents.Count;
        var paidStudents = activeStudents.Count(x => x.PaymentStatus == PaymentStatus.Paid);
        var actualRevenue = cls.Students.Sum(x => x.PaidAmount);
        var potentialRevenue = activeStudents.Sum(x => GetPayableTuition(cls.TuitionFee, x.DiscountAmount));
        var averageRevenue = enrolledStudents > 0 ? potentialRevenue / enrolledStudents : cls.TuitionFee;
        // Session-based teacher pay is committed per class, not proportional to enrolment.
        var projectedTeacherCost = Math.Max(teacherCost.AccruedAmount, teacherCost.ProjectedAmount);
        var projectedFixedCost = fixedCost + unclassifiedExpense + projectedTeacherCost;
        var variablePerStudent = enrolledStudents > 0 ? variableCost / enrolledStudents : 0;
        var contribution = averageRevenue - variablePerStudent;
        decimal? breakEvenStudents = contribution > 0 && teacherCost.LastSyncedAt.HasValue &&
            (enrolledStudents > 0 || variableCost == 0)
            ? Math.Ceiling(projectedFixedCost / contribution)
            : null;
        decimal? breakEvenRevenue = breakEvenStudents.HasValue
            ? breakEvenStudents.Value * averageRevenue
            : null;

        if (enrolledStudents == 0)
            warnings.Add("Lớp chưa có học viên; chưa thể tính chi phí biến đổi bình quân trên học viên.");
        if (teacherCost.LastSyncedAt is null)
            warnings.Add("Chưa có snapshot chi phí giảng viên từ CSCA Course trong phạm vi lớp này.");
        if (contribution <= 0)
            warnings.Add("Biên đóng góp trên mỗi học viên không dương; lớp chưa có điểm hòa vốn hữu hạn.");
        warnings.Add("Hòa vốn dùng chi phí lịch dạy dự kiến trong kỳ như chi phí cố định của lớp; khoản chi chưa phân loại được tính vào chi phí cam kết. Học phí và số đã đóng là lũy kế toàn lớp, không phải dòng tiền riêng của kỳ.");

        return Result<ClassBreakEvenAnalysisDto>.Success(new ClassBreakEvenAnalysisDto
        {
            ClassId = cls.Id,
            ClassCode = cls.Code,
            ClassName = cls.Name,
            From = rangeFrom,
            To = rangeTo,
            EnrolledStudents = enrolledStudents,
            PaidStudents = paidStudents,
            ActualRevenue = actualRevenue,
            PotentialRevenue = potentialRevenue,
            TeacherAccruedCost = teacherCost.AccruedAmount,
            TeacherProjectedCost = projectedTeacherCost,
            LedgerFixedCost = fixedCost,
            LedgerVariableCost = variableCost,
            LedgerUnclassifiedExpense = unclassifiedExpense,
            AverageRevenuePerStudent = averageRevenue,
            VariableCostPerStudent = variablePerStudent,
            ContributionMarginPerStudent = contribution,
            BreakEvenStudents = breakEvenStudents,
            BreakEvenRevenue = breakEvenRevenue,
            BreakEvenReachable = breakEvenStudents.HasValue,
            TeacherCostLastSyncedAt = teacherCost.LastSyncedAt,
            Warnings = warnings
        });
    }

    private static bool IsPayrollReference(FinanceTransaction transaction) =>
        transaction.ReferenceType?.Contains("Payroll", StringComparison.OrdinalIgnoreCase) == true ||
        transaction.ReferenceType?.Contains("Payslip", StringComparison.OrdinalIgnoreCase) == true;

    private static LedgerCostKind ClassifyLedgerCost(FinanceTransaction transaction)
    {
        var raw = $"{transaction.Category?.Code} {transaction.Category?.Name} {transaction.Description}".Normalize(NormalizationForm.FormD);
        var value = new string(raw.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC).ToUpperInvariant().Replace('Đ', 'D');
        if (ContainsAny(value, "CO_DINH", "CO DINH", "FIXED", "THUE_MAT_BANG", "RENT", "OFFICE"))
            return LedgerCostKind.Fixed;
        if (ContainsAny(value, "BIEN DOI", "BIEN_DOI", "VARIABLE", "HOA_HONG", "HOA HONG", "COMMISSION", "ADS", "MARKETING"))
            return LedgerCostKind.Variable;
        return LedgerCostKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(value.Contains);

    private enum LedgerCostKind
    {
        Unknown,
        Fixed,
        Variable
    }
}
