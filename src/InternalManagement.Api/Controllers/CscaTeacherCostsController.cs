using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using Microsoft.AspNetCore.Mvc;

namespace InternalManagement.Api.Controllers;

/// <summary>
/// Read-only CSCA Course payroll projection. Salary facts are configured in
/// the LMS and cannot be entered or edited through this controller.
/// </summary>
[Route("api/v1/payroll/csca-teacher-costs")]
public sealed class CscaTeacherCostsController : BaseApiController
{
    private readonly ICscaTeacherCostSnapshotService _service;

    public CscaTeacherCostsController(ICscaTeacherCostSnapshotService service)
    {
        _service = service;
    }

    [HttpGet]
    // Finance users need to read the imported teaching cost to reconcile class
    // profitability; only the sync operation remains restricted to system sync.
    [HasPermission(Permissions.FinanceReportsView)]
    [ProducesResponseType(typeof(ApiResponse<CscaTeacherCostSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaTeacherCostSummaryDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? courseSourceId,
        [FromQuery] string? classSourceId,
        [FromQuery] string? teacherSourceId,
        [FromQuery] bool includeProjected = true,
        CancellationToken ct = default)
    {
        var result = await _service.GetAsync(
            from, to, courseSourceId, classSourceId, teacherSourceId, includeProjected, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaTeacherCostSummaryDto>.Fail(
                result.Errors.FirstOrDefault() ?? "Không thể đọc chi phí giảng viên CSCA Course."));

        return Ok(ApiResponse<CscaTeacherCostSummaryDto>.Ok(
            result.Value!, "Lấy chi phí giảng viên CSCA Course thành công."));
    }

    [HttpPost("sync")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    [ProducesResponseType(typeof(ApiResponse<CscaTeacherCostSyncResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaTeacherCostSyncResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync(
        [FromBody] SyncCscaTeacherCostsRequest request,
        CancellationToken ct = default)
    {
        var result = await _service.SyncAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaTeacherCostSyncResultDto>.Fail(
                result.Errors.FirstOrDefault() ?? "Không thể đồng bộ chi phí giảng viên từ CSCA Course."));

        return Ok(ApiResponse<CscaTeacherCostSyncResultDto>.Ok(
            result.Value!, "Đã đồng bộ snapshot chi phí giảng viên từ CSCA Course."));
    }
}
