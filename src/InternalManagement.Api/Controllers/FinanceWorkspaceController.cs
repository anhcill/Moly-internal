using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/finance/workspace")]
public sealed class FinanceWorkspaceController(IFinanceWorkspaceService service) : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.FinanceReportsView)]
    public async Task<IActionResult> Get([FromQuery] DateTime? from, [FromQuery] int horizonMonths = 6, [FromQuery] decimal collectionPercent = 100, [FromQuery] decimal costFactor = 1, [FromQuery] decimal openingCash = 0, CancellationToken ct = default)
        => Wrap(await service.GetWorkspaceAsync(from, horizonMonths, collectionPercent, costFactor, openingCash, ct));

    [HttpGet("receivables")]
    [HasPermission(Permissions.FinanceReportsView)]
    public async Task<IActionResult> Receivables(CancellationToken ct) => Wrap(await service.GetReceivablesAsync(ct));

    [HttpPost("receivables/{id:guid}/receipts")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> Receipt(Guid id, [FromBody] RecordStudentReceiptRequest request, CancellationToken ct) => Wrap(await service.RecordReceiptAsync(id, request, ct));

    [HttpPost("receivables/{id:guid}/refunds")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> Refund(Guid id, [FromBody] RecordStudentRefundRequest request, CancellationToken ct) => Wrap(await service.RecordRefundAsync(id, request, ct));

    [HttpPut("receivables/{id:guid}")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> UpdateReceivable(Guid id, [FromBody] UpdateStudentReceivableRequest request, CancellationToken ct) => Wrap(await service.UpdateReceivableAsync(id, request, ct));

    [HttpGet("teachers")]
    [HasPermission(Permissions.FinanceReportsView)]
    public async Task<IActionResult> Teachers(CancellationToken ct) => Wrap(await service.GetTeacherCostsAsync(ct));

    [HttpPost("teacher-advances")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> Advance([FromBody] IssueTeacherAdvanceRequest request, CancellationToken ct) => Wrap(await service.IssueAdvanceAsync(request, ct));

    [HttpPost("teacher-advances/{id:guid}/returns")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> ReturnAdvance(Guid id, [FromBody] ReturnTeacherAdvanceRequest request, CancellationToken ct) => Wrap(await service.ReturnAdvanceAsync(id, request, ct));

    [HttpPost("teacher-payments")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> PayTeacher([FromBody] PayTeacherRequest request, CancellationToken ct) => Wrap(await service.PayTeacherAsync(request, ct));

    [HttpGet("planned-movements")]
    [HasPermission(Permissions.FinanceReportsView)]
    public async Task<IActionResult> Planned(CancellationToken ct) => Wrap(await service.GetPlannedMovementsAsync(ct));

    [HttpPost("planned-movements")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> AddPlan([FromBody] CreatePlannedCashMovementRequest request, CancellationToken ct) => Wrap(await service.CreatePlannedMovementAsync(request, ct));

    [HttpPost("planned-movements/{id:guid}/settle")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> SettlePlan(Guid id, CancellationToken ct) => Wrap(await service.SettlePlannedMovementAsync(id, ct));

    [HttpPost("planned-movements/{id:guid}/cancel")]
    [HasPermission(Permissions.FinanceTransactionsManage)]
    public async Task<IActionResult> CancelPlan(Guid id, CancellationToken ct) => Wrap(await service.CancelPlannedMovementAsync(id, ct));

    private IActionResult Wrap<T>(Result<T> result) => result.Succeeded ? Ok(ApiResponse<T>.Ok(result.Value!, "Thành công.")) : BadRequest(ApiResponse<T>.Fail(result.Errors.FirstOrDefault() ?? "Không thể thực hiện thao tác."));
}
