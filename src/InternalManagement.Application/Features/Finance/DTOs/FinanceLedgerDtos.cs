using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Finance.DTOs;

/// <summary>
/// DTOs cho sổ thu/chi. API nhận cả mã tiếng Anh (Income/Expense) và tiếng Việt
/// (Thu/Chi), còn response luôn trả thêm nhãn tiếng Việt để WPF dùng trực tiếp.
/// </summary>
public sealed record FinanceCategoryDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Code,
    string Name,
    string TransactionType,
    string TransactionTypeName,
    bool IsActive);

public sealed record CreateFinanceCategoryRequest(
    string Code,
    string Name,
    string TransactionType,
    Guid? BusinessUnitId = null);

public sealed record FinanceTransactionDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    Guid? CategoryId,
    string? CategoryCode,
    string? CategoryName,
    string TransactionType,
    string TransactionTypeName,
    decimal Amount,
    DateTime TransactionDate,
    string? ReferenceType,
    Guid? ReferenceId,
    string? ReferenceTitle,
    Guid? CashAccountId,
    string? CashAccountCode,
    string? CashAccountName,
    string Description,
    DateTime CreatedAt);

public sealed record CreateFinanceTransactionRequest(
    string TransactionType,
    decimal Amount,
    DateTime? TransactionDate,
    Guid? CategoryId,
    Guid? BusinessUnitId,
    string? ReferenceType,
    Guid? ReferenceId,
    string Description,
    Guid? CashAccountId = null);

public sealed record CashAccountDto(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    string? AccountNumber,
    string? BankName,
    decimal CurrentBalance,
    DateTime CreatedAt);

public sealed record CreateCashAccountRequest(
    string Code,
    string Name,
    string? AccountNumber = null,
    string? BankName = null,
    decimal OpeningBalance = 0);

public sealed record AdjustCashAccountRequest(
    string TransactionType,
    decimal Amount,
    DateTime? TransactionDate,
    Guid? CategoryId,
    Guid? BusinessUnitId,
    string Description);

public sealed record CashFlowDayDto(
    DateTime Date,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashFlow);

public sealed record CashFlowReportDto(
    DateTime From,
    DateTime To,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashFlow,
    IReadOnlyList<CashFlowDayDto> ByDay);

/// <summary>
/// Một mảng hoạt động trên bảng tổng tài chính. Thu/chi và dòng tiền ròng lấy từ
/// sổ FinanceTransaction; lợi nhuận lấy từ snapshot nghiệp vụ tương ứng để không
/// cộng trùng một khoản tiền đã xuất hiện trong cả hai nguồn.
/// </summary>
public sealed record FinancialAreaSummaryDto(
    string AreaCode,
    string AreaName,
    IReadOnlyList<string> BusinessUnitCodes,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashFlow,
    decimal OperatingProfit,
    decimal ConfirmedProfit,
    decimal ProvisionalProfit,
    bool HasProvisionalData,
    string ProfitDataStatus,
    string ProfitDataStatusName,
    string ProfitSourceName,
    int CashTransactionCount,
    int ProfitSnapshotCount);

public sealed record UnclassifiedCashFlowDto(
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashFlow,
    int TransactionCount);

/// <summary>
/// Bảng tổng của hai mảng độc lập và tổng cộng toàn công ty trong cùng kỳ.
/// CompanyTotal luôn bằng tổng TECHNOLOGY_EDUCATION + FASHION; giao dịch chưa
/// gán đúng Business Unit được trả riêng để UI cảnh báo người dùng phân loại.
/// </summary>
public sealed record CompanyFinancialOverviewDto(
    DateTime From,
    DateTime To,
    IReadOnlyList<FinancialAreaSummaryDto> Areas,
    FinancialAreaSummaryDto CompanyTotal,
    UnclassifiedCashFlowDto UnclassifiedCashFlow,
    bool HasUnclassifiedTransactions,
    string CalculationNote);

public sealed record FinanceProfitReportItemDto(
    Guid? BusinessUnitId,
    string BusinessUnitCode,
    string BusinessUnitName,
    string Channel,
    Guid? ProductId,
    string? ProductName,
    Guid? ProductVariantId,
    string? Sku,
    string? Size,
    string? ProductionBatchCode,
    int OrderCount,
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
    decimal ConfirmedProfit,
    decimal ProvisionalProfit,
    bool HasProvisionalCost,
    string CostStatus,
    string CostStatusName)
{
    public decimal GrossProfit => NetSales - Cogs;
    public decimal ProfitAfterPlatformFees => GrossProfit - PlatformFee - PaymentFee;
    public decimal ProfitAfterMarketing => ProfitAfterPlatformFees - AffiliateFee - AdvertisingCost;
    public decimal NetProfit => ConfirmedProfit + ProvisionalProfit;
};

public sealed record FinanceProfitReportDto(
    DateTime From,
    DateTime To,
    IReadOnlyList<FinanceProfitReportItemDto> Items,
    decimal GrossSales,
    decimal NetSales,
    decimal Cogs,
    decimal ConfirmedProfit,
    decimal ProvisionalProfit,
    bool HasProvisionalCost);

public sealed record FinanceReferenceDto(
    string ReferenceType,
    Guid ReferenceId,
    bool Exists,
    string ReferenceTitle);
