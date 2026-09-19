using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/finance")]
public sealed class FinanceTransactionsController : BaseApiController
{
    private readonly IFinanceLedgerService _financeService;

    public FinanceTransactionsController(IFinanceLedgerService financeService)
    {
        _financeService = financeService;
    }

    [HttpGet("categories")]
    [HasPermission(Permissions.FinanceTransactionsView)]
    public async Task<IActionResult> GetCategories(
        [FromQuery] string? type,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (!TryParseType(type, out var parsedType))
            return BadRequest(ApiResponse<IReadOnlyList<FinanceCategoryDto>>.Fail(
                "Loại giao dịch không hợp lệ. Hãy dùng Thu/Chi hoặc Income/Expense."));

        var result = await _financeService.GetCategoriesAsync(parsedType, includeInactive, ct);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<FinanceCategoryDto>>.Ok(result.Value!, "Lấy danh sách khoản mục thu/chi thành công."))
            : BadRequest(ApiResponse<IReadOnlyList<FinanceCategoryDto>>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy khoản mục thu/chi."));
    }

    [HttpPost("categories")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateFinanceCategoryRequest request,
        CancellationToken ct)
    {
        var result = await _financeService.CreateCategoryAsync(request, ct);
        return result.Succeeded
            ? Ok(ApiResponse<FinanceCategoryDto>.Ok(result.Value!, "Tạo khoản mục thu/chi thành công."))
            : BadRequest(ApiResponse<FinanceCategoryDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo khoản mục thu/chi thất bại."));
    }

    [HttpGet("cash-accounts")]
    [HasPermission(Permissions.FinanceTransactionsView)]
    public async Task<IActionResult> GetCashAccounts(CancellationToken ct)
    {
        var result = await _financeService.GetCashAccountsAsync(ct);
        return result.Succeeded
            ? Ok(ApiResponse<IReadOnlyList<CashAccountDto>>.Ok(result.Value!, "Lấy danh sách tài khoản tiền thành công."))
            : BadRequest(ApiResponse<IReadOnlyList<CashAccountDto>>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy tài khoản tiền."));
    }

    [HttpPost("cash-accounts")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> CreateCashAccount(
        [FromBody] CreateCashAccountRequest request,
        CancellationToken ct)
    {
        var result = await _financeService.CreateCashAccountAsync(request, ct);
        return result.Succeeded
            ? Ok(ApiResponse<CashAccountDto>.Ok(result.Value!, "Tạo tài khoản tiền thành công."))
            : BadRequest(ApiResponse<CashAccountDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo tài khoản tiền thất bại."));
    }

    [HttpPost("cash-accounts/{cashAccountId:guid}/adjust")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> AdjustCashAccount(
        Guid cashAccountId,
        [FromBody] AdjustCashAccountRequest request,
        CancellationToken ct)
    {
        var result = await _financeService.AdjustCashAccountAsync(cashAccountId, request, ct);
        return result.Succeeded
            ? Ok(ApiResponse<FinanceTransactionDto>.Ok(result.Value!, "Điều chỉnh số dư tài khoản tiền thành công."))
            : BadRequest(ApiResponse<FinanceTransactionDto>.Fail(result.Errors.FirstOrDefault() ?? "Điều chỉnh tài khoản tiền thất bại."));
    }

    [HttpGet("transactions")]
    [HasPermission(Permissions.FinanceTransactionsView)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? type,
        [FromQuery] Guid? businessUnitId,
        [FromQuery] string? referenceType,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!TryParseType(type, out var parsedType))
            return BadRequest(ApiResponse<PaginatedResult<FinanceTransactionDto>>.Fail(
                "Loại giao dịch không hợp lệ. Hãy dùng Thu/Chi hoặc Income/Expense."));

        var result = await _financeService.GetTransactionsAsync(
            from, to, parsedType, businessUnitId, referenceType, pageIndex, pageSize, ct);
        return result.Succeeded
            ? Ok(ApiResponse<PaginatedResult<FinanceTransactionDto>>.Ok(result.Value!, "Lấy sổ giao dịch thành công."))
            : BadRequest(ApiResponse<PaginatedResult<FinanceTransactionDto>>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy sổ giao dịch."));
    }

    [HttpPost("transactions")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> CreateTransaction(
        [FromBody] CreateFinanceTransactionRequest request,
        CancellationToken ct)
    {
        var result = await _financeService.CreateTransactionAsync(request, ct);
        return result.Succeeded
            ? Ok(ApiResponse<FinanceTransactionDto>.Ok(result.Value!, "Ghi nhận giao dịch thu/chi thành công."))
            : BadRequest(ApiResponse<FinanceTransactionDto>.Fail(result.Errors.FirstOrDefault() ?? "Ghi nhận giao dịch thất bại."));
    }

    [HttpGet("cash-flow")]
    [HasPermission(Permissions.FinanceReportsView)]
    public async Task<IActionResult> GetCashFlow(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? businessUnitId,
        CancellationToken ct)
    {
        var result = await _financeService.GetCashFlowReportAsync(from, to, businessUnitId, ct);
        return result.Succeeded
            ? Ok(ApiResponse<CashFlowReportDto>.Ok(result.Value!, "Lấy báo cáo dòng tiền thành công."))
            : BadRequest(ApiResponse<CashFlowReportDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy báo cáo dòng tiền."));
    }

    [HttpGet("overview")]
    [HasPermission(Permissions.FinanceReportsView)]
    [ProducesResponseType(typeof(ApiResponse<CompanyFinancialOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CompanyFinancialOverviewDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCompanyFinancialOverview(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _financeService.GetCompanyFinancialOverviewAsync(from, to, ct);
        return result.Succeeded
            ? Ok(ApiResponse<CompanyFinancialOverviewDto>.Ok(
                result.Value!, "Lấy bảng tổng tài chính hai mảng thành công."))
            : BadRequest(ApiResponse<CompanyFinancialOverviewDto>.Fail(
                result.Errors.FirstOrDefault() ?? "Không thể lấy bảng tổng tài chính."));
    }

    [HttpGet("profit-report")]
    [HasPermission(Permissions.FinanceReportsView)]
    public async Task<IActionResult> GetProfitReport(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? businessUnitId,
        [FromQuery] string? channel,
        [FromQuery] Guid? productId,
        [FromQuery] Guid? productVariantId,
        [FromQuery] string? size,
        [FromQuery] string? productionBatchCode,
        CancellationToken ct)
    {
        var result = await _financeService.GetProfitReportAsync(
            from, to, businessUnitId, channel, productId, productVariantId, size, productionBatchCode, ct);
        return result.Succeeded
            ? Ok(ApiResponse<FinanceProfitReportDto>.Ok(result.Value!, "Lấy báo cáo lợi nhuận theo Business Unit/kênh/sản phẩm thành công."))
            : BadRequest(ApiResponse<FinanceProfitReportDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy báo cáo lợi nhuận."));
    }

    [HttpGet("references/{referenceType}/{referenceId:guid}")]
    [HasPermission(Permissions.FinanceTransactionsView)]
    public async Task<IActionResult> ResolveReference(
        string referenceType,
        Guid referenceId,
        CancellationToken ct)
    {
        var result = await _financeService.ResolveReferenceAsync(referenceType, referenceId, ct);
        return result.Succeeded
            ? Ok(ApiResponse<FinanceReferenceDto>.Ok(result.Value!, "Tra cứu tham chiếu thành công."))
            : BadRequest(ApiResponse<FinanceReferenceDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể tra cứu tham chiếu."));
    }

    private static bool TryParseType(string? value, out TransactionType? type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            type = null;
            return true;
        }

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
                type = null;
                return false;
        }
    }
}
