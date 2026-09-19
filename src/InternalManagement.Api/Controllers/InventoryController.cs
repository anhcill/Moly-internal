using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class InventoryController : BaseApiController
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("warehouses")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WarehouseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWarehouses([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _inventoryService.GetWarehousesAsync(includeInactive, ct);
        return Ok(ApiResponse<IReadOnlyList<WarehouseDto>>.Ok(result, "Lấy danh sách kho thành công."));
    }

    [HttpPost("warehouses")]
    [HasPermission(Permissions.InventoryAdjust)]
    public async Task<IActionResult> CreateWarehouse([FromBody] CreateWarehouseRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.CreateWarehouseAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<WarehouseDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo kho thất bại."));
        return Ok(ApiResponse<WarehouseDto>.Ok(result.Value!, "Đã tạo kho."));
    }

    [HttpPut("warehouses/{id:guid}")]
    [HasPermission(Permissions.InventoryAdjust)]
    public async Task<IActionResult> UpdateWarehouse(Guid id, [FromBody] UpdateWarehouseRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.UpdateWarehouseAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<WarehouseDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật kho thất bại."));
        return Ok(ApiResponse<WarehouseDto>.Ok(result.Value!, "Đã cập nhật kho."));
    }

    [HttpDelete("warehouses/{id:guid}")]
    [HasPermission(Permissions.InventoryAdjust)]
    public async Task<IActionResult> DeleteWarehouse(Guid id, CancellationToken ct)
    {
        var result = await _inventoryService.DeleteWarehouseAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa kho thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa kho."));
    }

    [HttpGet("balances")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InventoryBalanceDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBalances(
        [FromQuery] string? search,
        [FromQuery] Guid? warehouseId,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _inventoryService.GetBalancesAsync(search, warehouseId, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<InventoryBalanceDto>>.Ok(result, "Lấy số dư tồn kho thành công."));
    }

    [HttpGet("movements")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InventoryMovementDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements(
        [FromQuery] Guid? variantId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _inventoryService.GetMovementsAsync(variantId, warehouseId, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<InventoryMovementDto>>.Ok(result, "Lấy sổ giao dịch kho thành công."));
    }

    [HttpGet("receipts")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<PurchaseReceiptDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPurchaseReceipts(
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _inventoryService.GetPurchaseReceiptsAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<PurchaseReceiptDto>>.Ok(result, "Lấy danh sách phiếu nhập kho thành công."));
    }

    [HttpGet("receipts/{id:guid}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ApiResponse<PurchaseReceiptDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PurchaseReceiptDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPurchaseReceiptById(Guid id, CancellationToken ct)
    {
        var result = await _inventoryService.GetPurchaseReceiptByIdAsync(id, ct);
        if (result == null)
            return NotFound(ApiResponse<PurchaseReceiptDetailDto>.Fail("Không tìm thấy phiếu nhập kho."));

        return Ok(ApiResponse<PurchaseReceiptDetailDto>.Ok(result, "Lấy chi tiết phiếu nhập kho thành công."));
    }

    [HttpPost("receipts/{id:guid}/attachment")]
    [HasPermission(Permissions.InventoryReceipt)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<PurchaseReceiptAttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PurchaseReceiptAttachmentDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPurchaseReceiptAttachment(
        Guid id,
        [FromForm] IFormFile? file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<PurchaseReceiptAttachmentDto>.Fail("Hãy chọn ảnh hoặc PDF hóa đơn để tải lên."));

        await using var stream = file.OpenReadStream();
        var result = await _inventoryService.UploadPurchaseReceiptAttachmentAsync(
            id, stream, file.FileName, file.ContentType, file.Length, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PurchaseReceiptAttachmentDto>.Fail(
                result.Errors.FirstOrDefault() ?? "Tải chứng từ lên thất bại."));

        return Ok(ApiResponse<PurchaseReceiptAttachmentDto>.Ok(result.Value!, "Đã lưu chứng từ lên Cloudinary."));
    }

    [HttpPost("receipts")]
    [HasPermission(Permissions.InventoryReceipt)]
    [ProducesResponseType(typeof(ApiResponse<PurchaseReceiptDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<PurchaseReceiptDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePurchaseReceipt([FromBody] CreatePurchaseReceiptRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.CreatePurchaseReceiptAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PurchaseReceiptDto>.Fail(result.Errors.FirstOrDefault() ?? "Lập phiếu nhập kho thất bại."));

        return CreatedAtAction(nameof(GetPurchaseReceiptById), new { id = result.Value!.Id }, ApiResponse<PurchaseReceiptDto>.Ok(result.Value, "Lập phiếu nhập kho thành công."));
    }

    [HttpPut("receipts/{id:guid}")]
    [HasPermission(Permissions.InventoryReceipt)]
    public async Task<IActionResult> UpdatePurchaseReceiptHeader(Guid id, [FromBody] UpdatePurchaseReceiptHeaderRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.UpdatePurchaseReceiptHeaderAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PurchaseReceiptDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật phiếu nhập thất bại."));
        return Ok(ApiResponse<PurchaseReceiptDto>.Ok(result.Value!, "Đã cập nhật thông tin phiếu nhập."));
    }

    [HttpPost("receipts/{id:guid}/cancel")]
    [HasPermission(Permissions.InventoryReceipt)]
    public async Task<IActionResult> CancelPurchaseReceipt(Guid id, [FromBody] CancelPurchaseReceiptRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.CancelPurchaseReceiptAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PurchaseReceiptDto>.Fail(result.Errors.FirstOrDefault() ?? "Hủy phiếu nhập thất bại."));
        return Ok(ApiResponse<PurchaseReceiptDto>.Ok(result.Value!, "Đã hủy phiếu nhập và hoàn tồn kho."));
    }

    [HttpPost("adjust")]
    [HasPermission(Permissions.InventoryAdjust)]
    [ProducesResponseType(typeof(ApiResponse<InventoryMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<InventoryMovementDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.AdjustStockAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<InventoryMovementDto>.Fail(result.Errors.FirstOrDefault() ?? "Điều chỉnh tồn kho thất bại."));

        return Ok(ApiResponse<InventoryMovementDto>.Ok(result.Value!, "Điều chỉnh tồn kho thành công."));
    }
}
