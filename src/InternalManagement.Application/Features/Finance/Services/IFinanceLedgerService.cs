using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Finance.Services;

public interface IFinanceLedgerService
{
    Task<Result<IReadOnlyList<FinanceCategoryDto>>> GetCategoriesAsync(
        TransactionType? type,
        bool includeInactive,
        CancellationToken ct);

    Task<Result<FinanceCategoryDto>> CreateCategoryAsync(
        CreateFinanceCategoryRequest request,
        CancellationToken ct);

    Task<Result<IReadOnlyList<CashAccountDto>>> GetCashAccountsAsync(CancellationToken ct);

    Task<Result<CashAccountDto>> CreateCashAccountAsync(
        CreateCashAccountRequest request,
        CancellationToken ct);

    Task<Result<PaginatedResult<FinanceTransactionDto>>> GetTransactionsAsync(
        DateTime? from,
        DateTime? to,
        TransactionType? type,
        Guid? businessUnitId,
        string? referenceType,
        int pageIndex,
        int pageSize,
        CancellationToken ct);

    Task<Result<FinanceTransactionDto>> CreateTransactionAsync(
        CreateFinanceTransactionRequest request,
        CancellationToken ct);

    Task<Result<FinanceTransactionDto>> AdjustCashAccountAsync(
        Guid cashAccountId,
        AdjustCashAccountRequest request,
        CancellationToken ct);

    Task<Result<CashFlowReportDto>> GetCashFlowReportAsync(
        DateTime? from,
        DateTime? to,
        Guid? businessUnitId,
        CancellationToken ct);

    Task<Result<CompanyFinancialOverviewDto>> GetCompanyFinancialOverviewAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken ct);

    Task<Result<FinanceProfitReportDto>> GetProfitReportAsync(
        DateTime? from,
        DateTime? to,
        Guid? businessUnitId,
        string? channel,
        Guid? productId,
        Guid? productVariantId,
        string? size,
        string? productionBatchCode,
        CancellationToken ct);

    Task<Result<FinanceReferenceDto>> ResolveReferenceAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken ct);
}
