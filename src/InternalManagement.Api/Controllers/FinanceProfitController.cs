using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/finance")]
public class FinanceProfitController : BaseApiController
{
    private readonly IProfitAllocationService _profitService;

    public FinanceProfitController(IProfitAllocationService profitService)
    {
        _profitService = profitService;
    }

    [HttpGet("profit-summary")]
    [HasPermission(Permissions.FinanceReportsView)]
    [ProducesResponseType(typeof(ApiResponse<List<BusinessUnitProfitSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllBusinessUnitsProfitSummary(CancellationToken ct)
    {
        var result = await _profitService.GetAllBusinessUnitsProfitSummaryAsync(ct);
        return result.Succeeded
            ? Ok(ApiResponse<List<BusinessUnitProfitSummaryDto>>.Ok(
                result.Value!, "Lấy báo cáo lợi nhuận các Business Units thành công."))
            : BadRequest(ApiResponse<List<BusinessUnitProfitSummaryDto>>.Fail(
                result.Errors.FirstOrDefault() ?? "Không thể xác định công ty hiện tại."));
    }

    [HttpGet("business-units/{buCode}/profit-summary")]
    [HasPermission(Permissions.FinanceReportsView)]
    [ProducesResponseType(typeof(ApiResponse<BusinessUnitProfitSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BusinessUnitProfitSummaryDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfitSummaryByBusinessUnit(string buCode, CancellationToken ct)
    {
        var result = await _profitService.GetProfitSummaryByBusinessUnitAsync(buCode, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<BusinessUnitProfitSummaryDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy dữ liệu."));

        return Ok(ApiResponse<BusinessUnitProfitSummaryDto>.Ok(result.Value!, $"Lấy báo cáo lợi nhuận Business Unit {buCode} thành công."));
    }

    [HttpPost("profit-allocations")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordAllocation([FromBody] RecordProfitAllocationRequest request, CancellationToken ct)
    {
        var result = await _profitService.RecordAllocationAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Ghi nhận phân bổ tài chính thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Ghi nhận phân bổ doanh thu/chi phí thành công."));
    }
}
