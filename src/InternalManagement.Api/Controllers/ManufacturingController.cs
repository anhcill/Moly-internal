using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/manufacturing")]
public class ManufacturingController : BaseApiController
{
    private readonly IManufacturingService _service;

    public ManufacturingController(IManufacturingService service)
    {
        _service = service;
    }

    [HttpGet("materials")]
    [HasPermission(Permissions.MaterialsView)]
    public async Task<IActionResult> GetMaterials([FromQuery] string? search, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _service.GetMaterialsAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<MaterialDto>>.Ok(result, "Lấy danh sách nguyên vật liệu thành công."));
    }

    [HttpGet("material-lots")]
    [HasPermission(Permissions.MaterialsView)]
    public async Task<IActionResult> GetMaterialLots([FromQuery] string? search, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _service.GetMaterialLotsAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<MaterialLotDto>>.Ok(result, "Lấy danh sách lô nguyên vật liệu thành công."));
    }

    [HttpPost("materials")]
    [HasPermission(Permissions.MaterialsManage)]
    public async Task<IActionResult> CreateMaterial([FromBody] CreateMaterialRequest request, CancellationToken ct)
    {
        var result = await _service.CreateMaterialAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<MaterialDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo nguyên vật liệu thất bại."));
        return Ok(ApiResponse<MaterialDto>.Ok(result.Value!, "Tạo nguyên vật liệu thành công."));
    }

    [HttpPost("materials/receive")]
    [HasPermission(Permissions.MaterialsManage)]
    public async Task<IActionResult> ReceiveMaterial([FromBody] ReceiveMaterialRequest request, CancellationToken ct)
    {
        var result = await _service.ReceiveMaterialAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<MaterialLotDto>.Fail(result.Errors.FirstOrDefault() ?? "Nhập nguyên vật liệu thất bại."));
        return Ok(ApiResponse<MaterialLotDto>.Ok(result.Value!, "Nhập nguyên vật liệu theo lô thành công."));
    }

    [HttpPost("boms")]
    [HasPermission(Permissions.ManufacturingManage)]
    public async Task<IActionResult> CreateBom([FromBody] CreateBomRequest request, CancellationToken ct)
    {
        var result = await _service.CreateBomAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<BomDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo BOM thất bại."));
        return Ok(ApiResponse<BomDto>.Ok(result.Value!, "Tạo BOM theo mẫu/size thành công."));
    }

    [HttpGet("boms/{id:guid}")]
    [HasPermission(Permissions.ManufacturingView)]
    public async Task<IActionResult> GetBom(Guid id, CancellationToken ct)
    {
        var result = await _service.GetBomAsync(id, ct);
        if (result == null) return NotFound(ApiResponse<BomDto>.Fail("Không tìm thấy BOM."));
        return Ok(ApiResponse<BomDto>.Ok(result, "Lấy BOM thành công."));
    }

    [HttpGet("boms")]
    [HasPermission(Permissions.ManufacturingView)]
    public async Task<IActionResult> GetBoms([FromQuery] string? search, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _service.GetBomsAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<BomDto>>.Ok(result, "Lấy danh sách BOM thành công."));
    }

    [HttpPost("production-orders")]
    [HasPermission(Permissions.ManufacturingManage)]
    public async Task<IActionResult> CreateProductionOrder([FromBody] CreateProductionOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CreateProductionOrderAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<ProductionOrderDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo lệnh sản xuất thất bại."));
        return Ok(ApiResponse<ProductionOrderDto>.Ok(result.Value!, "Tạo lệnh sản xuất thành công."));
    }

    [HttpGet("production-orders/{id:guid}")]
    [HasPermission(Permissions.ManufacturingView)]
    public async Task<IActionResult> GetProductionOrder(Guid id, CancellationToken ct)
    {
        var result = await _service.GetProductionOrderAsync(id, ct);
        if (result == null) return NotFound(ApiResponse<ProductionOrderDto>.Fail("Không tìm thấy lệnh sản xuất."));
        return Ok(ApiResponse<ProductionOrderDto>.Ok(result, "Lấy lệnh sản xuất thành công."));
    }

    [HttpGet("production-orders")]
    [HasPermission(Permissions.ManufacturingView)]
    public async Task<IActionResult> GetProductionOrders([FromQuery] string? search, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await _service.GetProductionOrdersAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<ProductionOrderDto>>.Ok(result, "Lấy danh sách lệnh sản xuất thành công."));
    }

    [HttpPost("production-orders/{id:guid}/complete")]
    [HasPermission(Permissions.ManufacturingManage)]
    public async Task<IActionResult> CompleteProductionOrder(Guid id, [FromBody] CompleteProductionOrderRequest request, CancellationToken ct)
    {
        var result = await _service.CompleteProductionOrderAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<ProductionOrderDto>.Fail(result.Errors.FirstOrDefault() ?? "Đóng lệnh sản xuất thất bại."));
        return Ok(ApiResponse<ProductionOrderDto>.Ok(result.Value!, "Đóng lệnh sản xuất và chốt actual cost thành công."));
    }
}
