using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ProductService> _logger;

    public ProductService(IApplicationDbContext db, ICurrentUserService currentUser, ILogger<ProductService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetCompanyAndBuIdAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        var fashionBu = await _db.BusinessUnits.FirstOrDefaultAsync(
            b => b.CompanyId == companyId && b.Code == "FASHION" && b.IsActive && !b.IsDeleted, ct);
        var buId = fashionBu?.Id ?? _currentUser.BusinessUnitId;

        return (companyId.Value, buId);
    }

    public async Task<PaginatedResult<ProductDto>> GetProductsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var query = _db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.CompanyId == companyId && p.BusinessUnitId == businessUnitId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Code.Contains(search) || p.Name.Contains(search) || (p.Category != null && p.Category.Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(
                p.Id,
                p.CompanyId,
                p.BusinessUnitId,
                p.Code,
                p.Name,
                p.Category,
                p.Description,
                p.IsActive,
                p.Variants.Count,
                p.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedResult<ProductDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<IReadOnlyList<ProductVariantDto>> GetActiveBuyVariantsAsync(CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);

        return await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive &&
                        v.SourcingType == SourcingType.Buy &&
                        v.Product.IsActive &&
                        !v.Product.IsDeleted &&
                        v.Product.CompanyId == companyId &&
                        v.Product.BusinessUnitId == businessUnitId)
            .OrderBy(v => v.Product.Name)
            .ThenBy(v => v.Sku)
            .Select(v => new ProductVariantDto(
                v.Id,
                v.ProductId,
                v.Product.Name,
                v.Sku,
                v.Barcode,
                v.Color,
                v.Size,
                v.CostPrice,
                v.SellingPrice,
                v.IsActive,
                v.Balances.Sum(b => b.OnHandQuantity),
                v.CreatedAt,
                v.SourcingType,
                v.CostStatus,
                v.LastActualCostAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductVariantDto>> GetActiveSellableVariantsAsync(CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);

        return await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && v.Product.IsActive && !v.Product.IsDeleted &&
                        v.Product.CompanyId == companyId && v.Product.BusinessUnitId == businessUnitId)
            .OrderBy(v => v.Product.Name)
            .ThenBy(v => v.Sku)
            .Select(v => new ProductVariantDto(
                v.Id,
                v.ProductId,
                v.Product.Name,
                v.Sku,
                v.Barcode,
                v.Color,
                v.Size,
                v.CostPrice,
                v.SellingPrice,
                v.IsActive,
                v.Balances.Sum(b => b.OnHandQuantity),
                v.CreatedAt,
                v.SourcingType,
                v.CostStatus,
                v.LastActualCostAt))
            .ToListAsync(ct);
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(Guid id, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var product = await _db.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.Balances)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted && p.CompanyId == companyId && p.BusinessUnitId == businessUnitId, ct);

        if (product == null)
            return null;

        var variants = product.Variants.Select(v => new ProductVariantDto(
            v.Id,
            v.ProductId,
            product.Name,
            v.Sku,
            v.Barcode,
            v.Color,
            v.Size,
            v.CostPrice,
            v.SellingPrice,
            v.IsActive,
            v.Balances.Sum(b => b.OnHandQuantity),
            v.CreatedAt,
            v.SourcingType,
            v.CostStatus,
            v.LastActualCostAt)).ToList();

        return new ProductDetailDto(
            product.Id,
            product.CompanyId,
            product.BusinessUnitId,
            product.Code,
            product.Name,
            product.Category,
            product.Description,
            product.IsActive,
            product.CreatedAt,
            variants);
    }

    public async Task<Result<ProductDto>> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        var (companyId, defaultBuId) = await GetCompanyAndBuIdAsync(ct);
        var validation = ValidateProduct(request.Code, request.Name);
        if (validation is not null)
            return Result<ProductDto>.Failure(validation);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Products.AnyAsync(product => product.CompanyId == companyId && product.Code == code && !product.IsDeleted, ct))
            return Result<ProductDto>.Failure($"Mã sản phẩm '{code}' đã tồn tại.");
        var product = new Product
        {
            CompanyId = companyId,
            BusinessUnitId = defaultBuId,
            Code = code,
            Name = request.Name.Trim(),
            Category = NullIfWhiteSpace(request.Category),
            Description = NullIfWhiteSpace(request.Description),
            IsActive = true
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        return Result<ProductDto>.Success(ToProductDto(product, 0));
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(Guid productId, UpdateProductRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var validation = ValidateProduct(request.Code, request.Name);
        if (validation is not null)
            return Result<ProductDto>.Failure(validation);
        var product = await _db.Products.Include(item => item.Variants)
            .FirstOrDefaultAsync(item => item.Id == productId && item.CompanyId == companyId && item.BusinessUnitId == businessUnitId && !item.IsDeleted, ct);
        if (product is null)
            return Result<ProductDto>.Failure("Không tìm thấy sản phẩm.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Products.AnyAsync(item => item.CompanyId == companyId && item.Code == code && item.Id != productId && !item.IsDeleted, ct))
            return Result<ProductDto>.Failure($"Mã sản phẩm '{code}' đã được dùng.");

        product.Code = code;
        product.Name = request.Name.Trim();
        product.Category = NullIfWhiteSpace(request.Category);
        product.Description = NullIfWhiteSpace(request.Description);
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        product.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<ProductDto>.Success(ToProductDto(product, product.Variants.Count));
    }

    public async Task<Result<bool>> DeleteProductAsync(Guid productId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var product = await _db.Products.Include(item => item.Variants)
            .FirstOrDefaultAsync(item => item.Id == productId && item.CompanyId == companyId && item.BusinessUnitId == businessUnitId && !item.IsDeleted, ct);
        if (product is null)
            return Result<bool>.Failure("Không tìm thấy sản phẩm.");
        var variantIds = product.Variants.Select(variant => variant.Id).ToArray();
        var hasHistory = variantIds.Length > 0 && (
            await _db.InventoryMovements.AnyAsync(item => variantIds.Contains(item.ProductVariantId), ct) ||
            await _db.PurchaseReceiptItems.AnyAsync(item => variantIds.Contains(item.ProductVariantId), ct) ||
            await _db.SalesOrderItems.AnyAsync(item => variantIds.Contains(item.ProductVariantId), ct));
        if (hasHistory)
            return Result<bool>.Failure("Sản phẩm đã có tồn kho hoặc chứng từ. Hãy sửa và chuyển sang Ngừng dùng thay vì xóa.");

        product.IsDeleted = true;
        product.IsActive = false;
        product.DeletedAt = DateTime.UtcNow;
        product.DeletedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ProductVariantDto>> AddVariantAsync(Guid productId, CreateProductVariantRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted && p.CompanyId == companyId && p.BusinessUnitId == businessUnitId, ct);
        
        if (product == null)
            return Result<ProductVariantDto>.Failure("Không tìm thấy sản phẩm.");

        var validation = ValidateVariant(request.Sku, request.CostPrice, request.SellingPrice);
        if (validation is not null)
            return Result<ProductVariantDto>.Failure(validation);
        var sku = request.Sku.Trim().ToUpperInvariant();
        if (await _db.ProductVariants.AnyAsync(item => item.Sku == sku, ct))
            return Result<ProductVariantDto>.Failure($"SKU '{sku}' đã tồn tại.");

        var variant = new ProductVariant
        {
            ProductId = productId,
            Sku = sku,
            Barcode = NullIfWhiteSpace(request.Barcode),
            Color = NullIfWhiteSpace(request.Color),
            Size = NullIfWhiteSpace(request.Size),
            SourcingType = request.SourcingType,
            CostPrice = request.CostPrice,
            CostStatus = request.SourcingType == SourcingType.Make ? CostStatus.Standard : CostStatus.Actual,
            SellingPrice = request.SellingPrice,
            IsActive = true
        };

        _db.ProductVariants.Add(variant);
        await _db.SaveChangesAsync(ct);

        return Result<ProductVariantDto>.Success(ToVariantDto(variant, product.Name, 0));
    }

    public async Task<Result<ProductVariantDto>> UpdateVariantAsync(Guid variantId, UpdateProductVariantRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var validation = ValidateVariant(request.Sku, request.CostPrice, request.SellingPrice);
        if (validation is not null)
            return Result<ProductVariantDto>.Failure(validation);
        var variant = await _db.ProductVariants.Include(item => item.Product).Include(item => item.Balances)
            .FirstOrDefaultAsync(item => item.Id == variantId && item.Product.CompanyId == companyId && item.Product.BusinessUnitId == businessUnitId && !item.Product.IsDeleted, ct);
        if (variant is null)
            return Result<ProductVariantDto>.Failure("Không tìm thấy SKU.");

        var sku = request.Sku.Trim().ToUpperInvariant();
        if (await _db.ProductVariants.AnyAsync(item => item.Sku == sku && item.Id != variantId, ct))
            return Result<ProductVariantDto>.Failure($"SKU '{sku}' đã được dùng.");
        if (variant.SourcingType != request.SourcingType && await VariantHasHistoryAsync(variantId, ct))
            return Result<ProductVariantDto>.Failure("SKU đã có nghiệp vụ kho/chứng từ nên không thể đổi nguồn hàng. Hãy tạo SKU mới nếu cần.");

        variant.Sku = sku;
        variant.Barcode = NullIfWhiteSpace(request.Barcode);
        variant.Color = NullIfWhiteSpace(request.Color);
        variant.Size = NullIfWhiteSpace(request.Size);
        variant.CostPrice = request.CostPrice;
        variant.SellingPrice = request.SellingPrice;
        variant.SourcingType = request.SourcingType;
        variant.IsActive = request.IsActive;
        variant.UpdatedAt = DateTime.UtcNow;
        variant.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<ProductVariantDto>.Success(ToVariantDto(variant, variant.Product.Name, variant.Balances.Sum(item => item.OnHandQuantity)));
    }

    public async Task<Result<bool>> DeleteVariantAsync(Guid variantId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var variant = await _db.ProductVariants.Include(item => item.Product).Include(item => item.Balances)
            .FirstOrDefaultAsync(item => item.Id == variantId && item.Product.CompanyId == companyId && item.Product.BusinessUnitId == businessUnitId && !item.Product.IsDeleted, ct);
        if (variant is null)
            return Result<bool>.Failure("Không tìm thấy SKU.");
        if (await VariantHasHistoryAsync(variantId, ct))
            return Result<bool>.Failure("SKU đã có tồn kho hoặc chứng từ. Hãy sửa và chuyển sang Ngừng dùng thay vì xóa.");

        _db.InventoryBalances.RemoveRange(variant.Balances);
        _db.ProductVariants.Remove(variant);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<PaginatedResult<SupplierDto>> GetSuppliersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var query = _db.Suppliers.Where(s => s.CompanyId == companyId && s.BusinessUnitId == businessUnitId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id,
                s.CompanyId,
                s.BusinessUnitId,
                s.Code,
                s.Name,
                s.ContactName,
                s.Phone,
                s.PhoneNumbers,
                s.Email,
                s.Address,
                s.BankAccounts,
                s.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedResult<SupplierDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<Result<SupplierDto>> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken ct)
    {
        var (companyId, defaultBuId) = await GetCompanyAndBuIdAsync(ct);
        var validation = ValidateSupplier(request.Code, request.Name);
        if (validation is not null)
            return Result<SupplierDto>.Failure(validation);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Suppliers.AnyAsync(s => s.CompanyId == companyId && s.Code == code, ct))
            return Result<SupplierDto>.Failure($"Mã nhà cung cấp '{code}' đã tồn tại.");

        var phoneNumbers = NormalizeLineList(request.PhoneNumbers ?? request.Phone);
        var supplier = new Supplier
        {
            CompanyId = companyId,
            BusinessUnitId = defaultBuId,
            Code = code,
            Name = request.Name.Trim(),
            ContactName = NullIfWhiteSpace(request.ContactName),
            Phone = FirstLine(phoneNumbers) ?? NullIfWhiteSpace(request.Phone),
            PhoneNumbers = phoneNumbers,
            Email = NullIfWhiteSpace(request.Email),
            Address = NullIfWhiteSpace(request.Address),
            BankAccounts = NormalizeLineList(request.BankAccounts)
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);

        return Result<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<Result<SupplierDto>> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var validation = ValidateSupplier(request.Code, request.Name);
        if (validation is not null)
            return Result<SupplierDto>.Failure(validation);

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId && s.BusinessUnitId == businessUnitId, ct);
        if (supplier is null)
            return Result<SupplierDto>.Failure("Không tìm thấy nhà cung cấp.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Suppliers.AnyAsync(s => s.CompanyId == companyId && s.Code == code && s.Id != supplierId, ct))
            return Result<SupplierDto>.Failure($"Mã nhà cung cấp '{code}' đã được dùng.");

        var phoneNumbers = NormalizeLineList(request.PhoneNumbers ?? request.Phone);
        supplier.Code = code;
        supplier.Name = request.Name.Trim();
        supplier.ContactName = NullIfWhiteSpace(request.ContactName);
        supplier.Phone = FirstLine(phoneNumbers) ?? NullIfWhiteSpace(request.Phone);
        supplier.PhoneNumbers = phoneNumbers;
        supplier.Email = NullIfWhiteSpace(request.Email);
        supplier.Address = NullIfWhiteSpace(request.Address);
        supplier.BankAccounts = NormalizeLineList(request.BankAccounts);
        supplier.UpdatedAt = DateTime.UtcNow;
        supplier.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<Result<bool>> DeleteSupplierAsync(Guid supplierId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetCompanyAndBuIdAsync(ct);
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId && s.BusinessUnitId == businessUnitId, ct);
        if (supplier is null)
            return Result<bool>.Failure("Không tìm thấy nhà cung cấp.");
        if (await _db.PurchaseReceipts.AnyAsync(receipt => receipt.SupplierId == supplierId, ct))
            return Result<bool>.Failure("Không thể xóa nhà cung cấp đã có phiếu nhập. Hãy giữ lại để bảo toàn lịch sử chứng từ.");

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static SupplierDto ToSupplierDto(Supplier supplier) => new(
        supplier.Id, supplier.CompanyId, supplier.BusinessUnitId, supplier.Code, supplier.Name,
        supplier.ContactName, supplier.Phone, supplier.PhoneNumbers, supplier.Email, supplier.Address,
        supplier.BankAccounts, supplier.CreatedAt);

    private static ProductDto ToProductDto(Product product, int variantCount) => new(
        product.Id, product.CompanyId, product.BusinessUnitId, product.Code, product.Name,
        product.Category, product.Description, product.IsActive, variantCount, product.CreatedAt);

    private static ProductVariantDto ToVariantDto(ProductVariant variant, string productName, int onHandQuantity) => new(
        variant.Id, variant.ProductId, productName, variant.Sku, variant.Barcode, variant.Color,
        variant.Size, variant.CostPrice, variant.SellingPrice, variant.IsActive, onHandQuantity,
        variant.CreatedAt, variant.SourcingType, variant.CostStatus, variant.LastActualCostAt);

    private async Task<bool> VariantHasHistoryAsync(Guid variantId, CancellationToken ct) =>
        await _db.InventoryMovements.AnyAsync(item => item.ProductVariantId == variantId, ct) ||
        await _db.PurchaseReceiptItems.AnyAsync(item => item.ProductVariantId == variantId, ct) ||
        await _db.SalesOrderItems.AnyAsync(item => item.ProductVariantId == variantId, ct) ||
        await _db.InventoryBalances.AnyAsync(item => item.ProductVariantId == variantId && (item.OnHandQuantity != 0 || item.ReservedQuantity != 0), ct);

    private static string? ValidateProduct(string? code, string? name)
        => string.IsNullOrWhiteSpace(code) ? "Mã sản phẩm là bắt buộc."
            : string.IsNullOrWhiteSpace(name) ? "Tên sản phẩm là bắt buộc."
            : null;

    private static string? ValidateVariant(string? sku, decimal costPrice, decimal sellingPrice)
        => string.IsNullOrWhiteSpace(sku) ? "Mã SKU là bắt buộc."
            : costPrice < 0 ? "Giá vốn không được âm."
            : sellingPrice < 0 ? "Giá bán không được âm."
            : null;

    private static string? ValidateSupplier(string? code, string? name)
        => string.IsNullOrWhiteSpace(code) ? "Mã nhà cung cấp là bắt buộc."
            : string.IsNullOrWhiteSpace(name) ? "Tên nhà cung cấp là bắt buộc."
            : null;

    private static string? NormalizeLineList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? FirstLine(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
