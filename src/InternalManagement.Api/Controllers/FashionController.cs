using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class FashionController : BaseApiController
{
    private readonly IProductService _productService;

    public FashionController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet("products")]
    [HasPermission(Permissions.ProductsView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<ProductDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _productService.GetProductsAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<ProductDto>>.Ok(result, "Lấy danh sách sản phẩm thành công."));
    }

    [HttpGet("products/{id:guid}")]
    [HasPermission(Permissions.ProductsView)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProductDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken ct)
    {
        var product = await _productService.GetProductByIdAsync(id, ct);
        if (product == null)
            return NotFound(ApiResponse<ProductDetailDto>.Fail("Không tìm thấy sản phẩm."));

        return Ok(ApiResponse<ProductDetailDto>.Ok(product, "Lấy thông tin chi tiết sản phẩm thành công."));
    }

    [HttpGet("variants/active-buy")]
    [HasPermission(Permissions.ProductsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProductVariantDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveBuyVariants(CancellationToken ct)
    {
        var variants = await _productService.GetActiveBuyVariantsAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<ProductVariantDto>>.Ok(variants, "Lấy danh sách SKU mua ngoài thành công."));
    }

    [HttpGet("variants/active")]
    [HasPermission(Permissions.ProductsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProductVariantDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSellableVariants(CancellationToken ct)
    {
        var variants = await _productService.GetActiveSellableVariantsAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<ProductVariantDto>>.Ok(variants, "Lấy danh sách SKU đang hoạt động để bán thành công."));
    }

    [HttpPost("products")]
    [HasPermission(Permissions.ProductsManage)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var result = await _productService.CreateProductAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<ProductDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo sản phẩm thất bại."));

        return CreatedAtAction(nameof(GetProductById), new { id = result.Value!.Id }, ApiResponse<ProductDto>.Ok(result.Value, "Tạo sản phẩm mới thành công."));
    }

    [HttpPut("products/{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var result = await _productService.UpdateProductAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<ProductDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật sản phẩm thất bại."));
        return Ok(ApiResponse<ProductDto>.Ok(result.Value!, "Cập nhật sản phẩm thành công."));
    }

    [HttpDelete("products/{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        var result = await _productService.DeleteProductAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa sản phẩm thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa sản phẩm."));
    }

    [HttpPost("products/{id:guid}/variants")]
    [HasPermission(Permissions.ProductsManage)]
    [ProducesResponseType(typeof(ApiResponse<ProductVariantDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ProductVariantDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] CreateProductVariantRequest request, CancellationToken ct)
    {
        var result = await _productService.AddVariantAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<ProductVariantDto>.Fail(result.Errors.FirstOrDefault() ?? "Thêm biến thể sản phẩm thất bại."));

        return Ok(ApiResponse<ProductVariantDto>.Ok(result.Value!, "Thêm biến thể SKU mới thành công."));
    }

    [HttpPut("variants/{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> UpdateVariant(Guid id, [FromBody] UpdateProductVariantRequest request, CancellationToken ct)
    {
        var result = await _productService.UpdateVariantAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<ProductVariantDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật SKU thất bại."));
        return Ok(ApiResponse<ProductVariantDto>.Ok(result.Value!, "Cập nhật SKU thành công."));
    }

    [HttpDelete("variants/{id:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> DeleteVariant(Guid id, CancellationToken ct)
    {
        var result = await _productService.DeleteVariantAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa SKU thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa SKU."));
    }

    [HttpGet("suppliers")]
    [HasPermission(Permissions.ProductsView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<SupplierDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuppliers(
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _productService.GetSuppliersAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<SupplierDto>>.Ok(result, "Lấy danh sách nhà cung cấp thành công."));
    }

    [HttpPost("suppliers")]
    [HasPermission(Permissions.ProductsManage)]
    [ProducesResponseType(typeof(ApiResponse<SupplierDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<SupplierDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest request, CancellationToken ct)
    {
        var result = await _productService.CreateSupplierAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<SupplierDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo nhà cung cấp thất bại."));

        return Ok(ApiResponse<SupplierDto>.Ok(result.Value!, "Tạo nhà cung cấp mới thành công."));
    }

    [HttpPut("suppliers/{supplierId:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> UpdateSupplier(Guid supplierId, [FromBody] UpdateSupplierRequest request, CancellationToken ct)
    {
        var result = await _productService.UpdateSupplierAsync(supplierId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<SupplierDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật nhà cung cấp thất bại."));
        return Ok(ApiResponse<SupplierDto>.Ok(result.Value!, "Cập nhật nhà cung cấp thành công."));
    }

    [HttpDelete("suppliers/{supplierId:guid}")]
    [HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> DeleteSupplier(Guid supplierId, CancellationToken ct)
    {
        var result = await _productService.DeleteSupplierAsync(supplierId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa nhà cung cấp thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa nhà cung cấp."));
    }
}
