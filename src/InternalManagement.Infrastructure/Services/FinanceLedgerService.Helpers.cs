using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class FinanceLedgerService
{
    private async Task<Guid?> ResolveCompanyIdAsync(CancellationToken ct)
    {
        if (_currentUser?.CompanyId is { } currentCompanyId && currentCompanyId != Guid.Empty)
            return currentCompanyId;

        // Chỉ tự suy ra tenant khi database có đúng một công ty. Việc lấy công ty
        // đầu tiên khi có nhiều tenant có thể làm rò rỉ dữ liệu chéo công ty.
        var companyIds = await _db.Companies
            .AsNoTracking()
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync(ct);
        return companyIds.Count == 1 ? companyIds[0] : null;
    }

    private static CashTotals SummarizeCash(IEnumerable<FinanceTransaction> transactions)
    {
        var items = transactions.ToList();
        var income = items.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount);
        var expense = items.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount);
        return new CashTotals(income, expense, income - expense, items.Count);
    }

    private static FinancialAreaSummaryDto CreateAreaSummary(
        string areaCode,
        string areaName,
        IReadOnlyList<string> businessUnitCodes,
        CashTotals cash,
        decimal confirmedProfit,
        decimal provisionalProfit,
        int confirmedSnapshotCount,
        int provisionalSnapshotCount,
        string profitSourceName)
    {
        var profitSnapshotCount = confirmedSnapshotCount + provisionalSnapshotCount;
        var hasProvisional = provisionalSnapshotCount > 0;
        var status = profitSnapshotCount == 0
            ? (Code: "NO_DATA", Name: "Chưa có dữ liệu lợi nhuận")
            : provisionalSnapshotCount == 0
                ? (Code: "ACTUAL", Name: "Đã chốt")
                : confirmedSnapshotCount == 0
                    ? (Code: "PROVISIONAL", Name: "Tạm tính")
                    : (Code: "MIXED", Name: "Một phần đã chốt, một phần tạm tính");

        return new FinancialAreaSummaryDto(
            areaCode,
            areaName,
            businessUnitCodes,
            cash.TotalIncome,
            cash.TotalExpense,
            cash.NetCashFlow,
            confirmedProfit + provisionalProfit,
            confirmedProfit,
            provisionalProfit,
            hasProvisional,
            status.Code,
            status.Name,
            profitSourceName,
            cash.TransactionCount,
            profitSnapshotCount);
    }

    private static FinancialAreaSummaryDto CreateCompanyTotal(
        IReadOnlyCollection<FinancialAreaSummaryDto> areas)
    {
        var confirmed = areas.Sum(x => x.ConfirmedProfit);
        var provisional = areas.Sum(x => x.ProvisionalProfit);
        var snapshotCount = areas.Sum(x => x.ProfitSnapshotCount);
        var hasProvisionalData = areas.Any(x => x.HasProvisionalData);
        var hasConfirmedData = areas.Any(x => x.ProfitDataStatus is "ACTUAL" or "MIXED");
        var cash = new CashTotals(
            areas.Sum(x => x.TotalIncome),
            areas.Sum(x => x.TotalExpense),
            areas.Sum(x => x.NetCashFlow),
            areas.Sum(x => x.CashTransactionCount));

        var total = CreateAreaSummary(
            "COMPANY_TOTAL",
            "Tổng toàn công ty",
            TechnologyEducationBusinessUnitCodes.Concat(FashionBusinessUnitCodes).ToArray(),
            cash,
            confirmed,
            provisional,
            hasConfirmedData ? 1 : 0,
            hasProvisionalData ? 1 : 0,
            "Tổng snapshot của hai mảng");
        return total with { ProfitSnapshotCount = snapshotCount };
    }

    private async Task<string?> ValidateBusinessUnitAsync(
        Guid companyId,
        Guid? businessUnitId,
        CancellationToken ct)
    {
        if (!businessUnitId.HasValue)
            return null;

        var exists = await _db.BusinessUnits.AnyAsync(
            x => x.CompanyId == companyId && x.Id == businessUnitId.Value, ct);
        return exists ? null : "Business Unit không thuộc công ty hiện tại.";
    }

    private async Task<Result<FinanceCategory?>> ValidateCategoryAsync(
        Guid companyId,
        Guid? categoryId,
        TransactionType transactionType,
        CancellationToken ct)
    {
        if (!categoryId.HasValue)
            return Result<FinanceCategory?>.Success(null);

        var category = await _db.FinanceCategories.FirstOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == categoryId.Value && x.IsActive, ct);
        if (category == null)
            return Result<FinanceCategory?>.Failure("Khoản mục thu/chi không tồn tại hoặc đã bị khóa.");
        if (category.Type != transactionType)
            return Result<FinanceCategory?>.Failure("Loại giao dịch không khớp với loại của khoản mục.");

        return Result<FinanceCategory?>.Success(category);
    }

    private async Task<FinanceTransaction?> FindIdempotentTransactionAsync(
        Guid companyId,
        string? referenceType,
        Guid? referenceId,
        TransactionType transactionType,
        CancellationToken ct)
    {
        if (referenceType == null || !referenceId.HasValue)
            return null;

        return await _db.FinanceTransactions
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId
                && x.ReferenceType == referenceType
                && x.ReferenceId == referenceId.Value
                && x.TransactionType == transactionType, ct);
    }

    private async Task<FinanceTransactionDto> ToTransactionDtoAsync(
        FinanceTransaction transaction,
        CancellationToken ct)
    {
        CashAccount? cashAccount = null;
        if (transaction.ReferenceId.HasValue && IsCashReference(transaction.ReferenceType))
        {
            cashAccount = await _db.CashAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == transaction.CompanyId
                    && x.Id == transaction.ReferenceId.Value, ct);
        }

        var referenceTitle = transaction.ReferenceType != null && transaction.ReferenceId.HasValue
            ? (await ResolveReferenceInternalAsync(
                transaction.ReferenceType,
                transaction.ReferenceId.Value,
                transaction.CompanyId,
                ct)).ReferenceTitle
            : null;

        return new FinanceTransactionDto(
            transaction.Id,
            transaction.CompanyId,
            transaction.BusinessUnitId,
            transaction.CategoryId,
            transaction.Category?.Code,
            transaction.Category?.Name,
            TypeCode(transaction.TransactionType),
            TypeName(transaction.TransactionType),
            transaction.Amount,
            transaction.TransactionDate,
            transaction.ReferenceType,
            transaction.ReferenceId,
            referenceTitle,
            cashAccount?.Id,
            cashAccount?.Code,
            cashAccount?.Name,
            transaction.Description,
            transaction.CreatedAt);
    }

    private async Task<FinanceReferenceDto> ResolveReferenceInternalAsync(
        string referenceType,
        Guid referenceId,
        Guid companyId,
        CancellationToken ct)
    {
        var normalized = NormalizeReferenceType(referenceType);
        switch (normalized)
        {
            case "CashAccount":
            case "CashAccountAdjustment":
            {
                var account = await _db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, account != null,
                    account == null ? $"Tài khoản tiền {referenceId}" : $"{account.Code} - {account.Name}");
            }
            case "SalesOrder":
            {
                var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, order != null,
                    order == null ? $"Đơn hàng {referenceId}" : $"Đơn hàng {order.OrderNumber}");
            }
            case "Return":
            {
                var item = await _db.Returns.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Phiếu trả hàng {referenceId}" : $"Phiếu trả hàng {item.ReturnNumber}");
            }
            case "SalesSettlement":
            {
                var settlement = await _db.SalesSettlements.AsNoTracking()
                    .Include(x => x.SalesOrder)
                    .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, settlement != null,
                    settlement == null
                        ? $"Giao dịch Fashion {referenceId}"
                        : $"{settlement.Kind} {settlement.PaymentReference} - đơn {settlement.SalesOrder.OrderNumber}");
            }
            case "ProductionOrder":
            {
                var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, order != null,
                    order == null ? $"Lệnh sản xuất {referenceId}" : $"Lệnh sản xuất {order.OrderNumber}");
            }
            case "CscaClass":
            {
                var item = await _db.CscaClasses.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Lớp CSCA {referenceId}" : $"{item.Code} - {item.Name}");
            }
            case "Payment":
            {
                var payment = await _db.Payments.AsNoTracking()
                    .Include(x => x.Customer)
                    .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, payment != null,
                    payment == null
                        ? $"Thanh toán {referenceId}"
                        : $"Thanh toán {payment.SourcePaymentId} - {payment.Customer.FullName}");
            }
            case "InterviewCustomer":
            {
                var item = await _db.InterviewCustomers.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Khách Interview {referenceId}" : $"{item.FullName} ({item.PackageName})");
            }
            case "PayrollPeriod":
            {
                var item = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Kỳ lương {referenceId}" : item.Name);
            }
            case "FinanceCategory":
            {
                var item = await _db.FinanceCategories.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Khoản mục {referenceId}" : $"{item.Code} - {item.Name}");
            }
            default:
                return new FinanceReferenceDto(normalized, referenceId, false, $"{normalized} - {referenceId}");
        }
    }

    private static FinanceCategoryDto ToCategoryDto(FinanceCategory category) => new(
        category.Id,
        category.CompanyId,
        category.BusinessUnitId,
        category.Code,
        category.Name,
        TypeCode(category.Type),
        TypeName(category.Type),
        category.IsActive);

    private static CashAccountDto ToCashAccountDto(CashAccount account) => new(
        account.Id,
        account.CompanyId,
        account.Code,
        account.Name,
        account.AccountNumber,
        account.BankName,
        account.CurrentBalance,
        account.CreatedAt);

    private static void ApplyCashDelta(CashAccount account, TransactionType type, decimal amount)
    {
        account.CurrentBalance += type == TransactionType.Income ? amount : -amount;
    }

    private static bool IsCashReference(string? referenceType) =>
        string.Equals(referenceType, "CashAccount", StringComparison.OrdinalIgnoreCase)
        || string.Equals(referenceType, "CashAccountAdjustment", StringComparison.OrdinalIgnoreCase);

    private static string TypeCode(TransactionType type) =>
        type == TransactionType.Income ? "Income" : "Expense";

    private static string TypeName(TransactionType type) =>
        type == TransactionType.Income ? "Thu" : "Chi";

    private static bool TryParseTransactionType(string? value, out TransactionType type)
    {
        type = TransactionType.Expense;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "income":
            case "thu":
            case "thu nhập":
            case "thu_nhap":
            case "1":
                type = TransactionType.Income;
                return true;
            case "expense":
            case "chi":
            case "chi phí":
            case "chi_phi":
            case "0":
                type = TransactionType.Expense;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeReferenceType(string value)
    {
        var normalized = value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty);
        return normalized.ToLowerInvariant() switch
        {
            "cashaccount" => "CashAccount",
            "cashaccountadjustment" => "CashAccountAdjustment",
            "salesorder" => "SalesOrder",
            "return" => "Return",
            "salessettlement" => "SalesSettlement",
            "productionorder" => "ProductionOrder",
            "cscaclass" => "CscaClass",
            "payment" => "Payment",
            "interviewcustomer" => "InterviewCustomer",
            "payrollperiod" => "PayrollPeriod",
            "financecategory" => "FinanceCategory",
            _ => value.Trim()
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeDate(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateRange NormalizeRange(DateTime? from, DateTime? to)
    {
        var fromValue = NormalizeDate(from ?? DateTime.UtcNow.Date.AddDays(-30));
        var rawTo = to ?? DateTime.UtcNow;
        // Query UI thường gửi ngày kết thúc ở 00:00. Xem đó là toàn bộ ngày để
        // giao dịch phát sinh buổi trưa/tối không bị mất khỏi báo cáo.
        if (to.HasValue && rawTo.TimeOfDay == TimeSpan.Zero && rawTo.Date < DateTime.MaxValue.Date)
            rawTo = rawTo.Date.AddDays(1).AddTicks(-1);
        var toValue = NormalizeDate(rawTo);
        return new DateRange(fromValue, toValue, toValue >= fromValue);
    }

    private static string GetCostStatusName(CostStatus status) => status switch
    {
        CostStatus.Actual => "Đã chốt",
        CostStatus.Standard => "Theo định mức",
        CostStatus.Estimated => "Tạm tính",
        CostStatus.Provisional => "Tạm thời",
        _ => status.ToString()
    };

    private static CostStatus GetRepresentativeCostStatus(IEnumerable<ProfitLine> lines)
    {
        var statuses = lines.Select(x => x.CostStatus).ToHashSet();
        if (statuses.Contains(CostStatus.Provisional))
            return CostStatus.Provisional;
        if (statuses.Contains(CostStatus.Estimated))
            return CostStatus.Estimated;
        if (statuses.Contains(CostStatus.Actual))
            return CostStatus.Actual;
        return CostStatus.Standard;
    }

    private sealed record DateRange(DateTime From, DateTime To, bool IsValid);

    private sealed record CashTotals(
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetCashFlow,
        int TransactionCount);

    private sealed record ProfitLine(
        Guid SnapshotId,
        Guid? BusinessUnitId,
        string BusinessUnitCode,
        string BusinessUnitName,
        string Channel,
        Guid? ProductId,
        string? ProductName,
        Guid ProductVariantId,
        string? Sku,
        string? Size,
        string? ProductionBatchCode,
        decimal GrossSales,
        decimal DiscountAmount,
        decimal RefundAmount,
        decimal NetSales,
        decimal Cogs,
        decimal PlatformFee,
        decimal AffiliateFee,
        decimal PaymentFee,
        decimal AdvertisingCost,
        decimal PackagingCost,
        decimal OtherSellingExpense,
        decimal ShippingSubsidy,
        decimal TaxAmount,
        decimal Profit,
        bool HasProvisionalCost,
        CostStatus CostStatus);
}
