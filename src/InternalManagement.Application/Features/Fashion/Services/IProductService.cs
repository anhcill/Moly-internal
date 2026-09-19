using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;

namespace InternalManagement.Application.Features.Fashion.Services;

public interface IProductService
{
    // Products
    Task<PaginatedResult<ProductDto>> GetProductsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<ProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProductVariantDto>> GetActiveBuyVariantsAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductVariantDto>> GetActiveSellableVariantsAsync(CancellationToken ct);
    Task<Result<ProductDto>> CreateProductAsync(CreateProductRequest request, CancellationToken ct);
    Task<Result<ProductDto>> UpdateProductAsync(Guid productId, UpdateProductRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteProductAsync(Guid productId, CancellationToken ct);
    Task<Result<ProductVariantDto>> AddVariantAsync(Guid productId, CreateProductVariantRequest request, CancellationToken ct);
    Task<Result<ProductVariantDto>> UpdateVariantAsync(Guid variantId, UpdateProductVariantRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteVariantAsync(Guid variantId, CancellationToken ct);

    // Suppliers
    Task<PaginatedResult<SupplierDto>> GetSuppliersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<SupplierDto>> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken ct);
    Task<Result<SupplierDto>> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteSupplierAsync(Guid supplierId, CancellationToken ct);
}
