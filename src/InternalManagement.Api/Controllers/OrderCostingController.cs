using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/orders")]
public class OrderCostingController : BaseApiController
{
    private readonly IOrderCostingService _service;

    public OrderCostingController(IOrderCostingService service)
    {
        _service = service;
    }

    [HttpPost]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> Create([FromBody] CreateSalesOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CreateOrderAndSnapshotAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<SalesOrderCostDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo đơn hàng thất bại."));
        return Ok(ApiResponse<SalesOrderCostDto>.Ok(result.Value!, "Tạo đơn hàng và snapshot lợi nhuận thành công."));
    }

    [HttpGet]
    [HasPermission(Permissions.OrdersView)]
    public async Task<IActionResult> GetOrders([FromQuery] string? search, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _service.GetOrdersAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<SalesOrderSummaryDto>>.Ok(result, "Lấy danh sách đơn hàng thành công."));
    }

    [HttpGet("{id:guid}/fulfillment")]
    [HasPermission(Permissions.OrdersView)]
    public async Task<IActionResult> GetFulfillment(Guid id, CancellationToken ct)
    {
        var result = await _service.GetFulfillmentAsync(id, ct);
        if (result == null) return NotFound(ApiResponse<SalesOrderFulfillmentDto>.Fail("Không tìm thấy đơn hàng."));
        return Ok(ApiResponse<SalesOrderFulfillmentDto>.Ok(result, "Lấy chi tiết giao hàng thành công."));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> UpdateHeader(Guid id, [FromBody] UpdateSalesOrderHeaderRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateOrderHeaderAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<SalesOrderSummaryDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật đơn hàng thất bại."));
        return Ok(ApiResponse<SalesOrderSummaryDto>.Ok(result.Value!, "Đã cập nhật đơn hàng."));
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelSalesOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CancelOrderAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<SalesOrderSummaryDto>.Fail(result.Errors.FirstOrDefault() ?? "Hủy đơn hàng thất bại."));
        return Ok(ApiResponse<SalesOrderSummaryDto>.Ok(result.Value!, "Đã hủy đơn hàng và giải phóng tồn giữ."));
    }

    [HttpGet("{id:guid}/cost")]
    [HasPermission(Permissions.OrdersView)]
    public async Task<IActionResult> GetCost(Guid id, CancellationToken ct)
    {
        var result = await _service.GetLatestSnapshotAsync(id, ct);
        if (result == null) return NotFound(ApiResponse<SalesOrderCostDto>.Fail("Không tìm thấy cost snapshot của đơn hàng."));
        return Ok(ApiResponse<SalesOrderCostDto>.Ok(result, "Lấy cost snapshot thành công."));
    }

    [HttpPost("{id:guid}/deliver")]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> Deliver(Guid id, [FromBody] FulfillSalesOrderRequest request, CancellationToken ct)
    {
        var result = await _service.DeliverAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<SalesOrderFulfillmentDto>.Fail(result.Errors.FirstOrDefault() ?? "Giao hàng thất bại."));
        return Ok(ApiResponse<SalesOrderFulfillmentDto>.Ok(result.Value!, "Giao hàng và cập nhật tồn kho thành công."));
    }

    [HttpPost("returns")]
    [HasPermission(Permissions.ReturnsManage)]
    public async Task<IActionResult> CreateReturn([FromBody] CreateReturnRequest request, CancellationToken ct)
    {
        var result = await _service.CreateReturnAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<ReturnDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo phiếu đổi trả thất bại."));
        return Ok(ApiResponse<ReturnDto>.Ok(result.Value!, "Tạo phiếu đổi trả thành công."));
    }

    [HttpPost("returns/{returnId:guid}/inspect")]
    [HasPermission(Permissions.ReturnsManage)]
    public async Task<IActionResult> InspectReturn(Guid returnId, [FromBody] InspectReturnRequest request, CancellationToken ct)
    {
        var result = await _service.InspectReturnAsync(returnId, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<ReturnDto>.Fail(result.Errors.FirstOrDefault() ?? "Kiểm tra đổi trả thất bại."));
        return Ok(ApiResponse<ReturnDto>.Ok(result.Value!, "Đã cập nhật kết quả kiểm tra đổi trả."));
    }

    [HttpGet("{id:guid}/settlements")]
    [HasPermission(Permissions.OrdersView)]
    public async Task<IActionResult> GetSettlements(Guid id, CancellationToken ct)
    {
        var result = await _service.GetSettlementsAsync(id, ct);
        return Ok(ApiResponse<IReadOnlyList<SalesSettlementDto>>.Ok(result, "Lấy các giao dịch thu/hoàn tiền của đơn hàng thành công."));
    }

    [HttpGet("{id:guid}/documents")]
    [HasPermission(Permissions.OrdersView)]
    public async Task<IActionResult> GetDocuments(Guid id, CancellationToken ct)
    {
        var result = await _service.GetDocumentsAsync(id, ct);
        return Ok(ApiResponse<IReadOnlyList<SalesDocumentDto>>.Ok(result, "Lấy chứng từ của đơn hàng thành công."));
    }

    [HttpPost("{id:guid}/settlements")]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> RecordSettlement(Guid id, [FromBody] RecordSalesSettlementRequest request, CancellationToken ct)
    {
        var result = await _service.RecordSettlementAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<SalesSettlementDto>.Fail(result.Errors.FirstOrDefault() ?? "Ghi nhận thu/hoàn tiền thất bại."));
        return Ok(ApiResponse<SalesSettlementDto>.Ok(result.Value!, "Đã ghi nhận giao dịch thu/hoàn tiền."));
    }

    [HttpPost("{id:guid}/documents")]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> IssueDocument(Guid id, [FromBody] IssueSalesDocumentRequest request, CancellationToken ct)
    {
        var result = await _service.IssueDocumentAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<SalesDocumentDto>.Fail(result.Errors.FirstOrDefault() ?? "Phát hành chứng từ thất bại."));
        return Ok(ApiResponse<SalesDocumentDto>.Ok(result.Value!, "Phát hành hóa đơn/phiếu bán lẻ thành công."));
    }

    [HttpPost("import")]
    [HasPermission(Permissions.OrdersManage)]
    public async Task<IActionResult> Import([FromBody] ImportSalesOrderRequest request, CancellationToken ct)
    {
        var result = await _service.ImportAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<ImportSalesOrderResult>.Fail(result.Errors.FirstOrDefault() ?? "Nhập đơn hàng thất bại."));
        return Ok(ApiResponse<ImportSalesOrderResult>.Ok(result.Value!, result.Value!.Message));
    }
}
