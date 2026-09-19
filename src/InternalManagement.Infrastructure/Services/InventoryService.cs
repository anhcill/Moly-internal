using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<InventoryService> _logger;
    private readonly IFileStorageService? _fileStorage;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;

    public InventoryService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<InventoryService> logger,
        IFileStorageService? fileStorage = null,
        IPartyResolver? partyResolver = null,
        IBusinessDocumentRegistry? documentRegistry = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _fileStorage = fileStorage;
        _partyResolver = partyResolver;
        _documentRegistry = documentRegistry;
    }

    private async Task<Guid> GetCompanyIdAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        return companyId.Value;
    }

    private async Task<Guid?> GetFashionBusinessUnitIdAsync(Guid companyId, CancellationToken ct)
    {
        var fashionBusinessUnit = await _db.BusinessUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Code == "FASHION" && b.IsActive && !b.IsDeleted, ct);

        return fashionBusinessUnit?.Id ?? _currentUser.BusinessUnitId;
    }

    private IQueryable<Warehouse> ScopeWarehouses(IQueryable<Warehouse> query, Guid companyId, Guid? businessUnitId)
    {
        query = ScopeAllWarehouses(query, companyId, businessUnitId);
        return query.Where(w => w.IsActive);
    }

    private static IQueryable<Warehouse> ScopeAllWarehouses(IQueryable<Warehouse> query, Guid companyId, Guid? businessUnitId)
    {
        query = query.Where(w => w.CompanyId == companyId);
        return businessUnitId.HasValue
            ? query.Where(w => w.BusinessUnitId == businessUnitId.Value)
            : query.Where(w => w.BusinessUnitId == null);
    }

    private async Task<Warehouse?> GetWarehouseForReadAsync(Guid companyId, Guid? businessUnitId, Guid? requestedWarehouseId, CancellationToken ct)
    {
        var query = ScopeWarehouses(_db.Warehouses.AsNoTracking(), companyId, businessUnitId);
        if (requestedWarehouseId.HasValue)
            return await query.FirstOrDefaultAsync(w => w.Id == requestedWarehouseId.Value, ct);

        return await query
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Warehouse?> GetOrCreateWarehouseAsync(
        Guid companyId,
        Guid? businessUnitId,
        Guid? requestedWarehouseId,
        CancellationToken ct)
    {
        var query = ScopeWarehouses(_db.Warehouses, companyId, businessUnitId);
        Warehouse? warehouse;

        if (requestedWarehouseId.HasValue)
        {
            warehouse = await query.FirstOrDefaultAsync(w => w.Id == requestedWarehouseId.Value, ct);
            if (warehouse == null)
                return null;
        }
        else
        {
            warehouse = await query
                .OrderByDescending(w => w.IsDefault)
                .ThenBy(w => w.Code)
                .FirstOrDefaultAsync(ct);
        }

        if (warehouse != null)
            return warehouse;

        warehouse = new Warehouse
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = businessUnitId.HasValue ? "FASHION-MAIN" : "MAIN",
            Name = businessUnitId.HasValue ? "Kho Thời trang chính" : "Kho chính",
            IsActive = true,
            IsDefault = true
        };
        _db.Warehouses.Add(warehouse);
        return warehouse;
    }

    private static (int PageIndex, int PageSize) NormalizePaging(int pageIndex, int pageSize)
        => (Math.Max(1, pageIndex), Math.Clamp(pageSize, 1, 200));

    private async Task<Dictionary<Guid, Document>> GetLatestReceiptAttachmentsAsync(
        Guid companyId,
        Guid? businessUnitId,
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken ct)
    {
        if (receiptIds.Count == 0)
            return new Dictionary<Guid, Document>(); 

        var documents = await _db.Documents
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId &&
                        d.BusinessUnitId == businessUnitId &&
                        d.EntityType == "PurchaseReceipt" &&
                        d.EntityId.HasValue &&
                        receiptIds.Contains(d.EntityId.Value))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return documents
            .Where(d => d.EntityId.HasValue)
            .GroupBy(d => d.EntityId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static PurchaseReceiptAttachmentDto ToAttachmentDto(Document document, Guid receiptId)
        => new(document.Id, receiptId, document.FileName, document.ContentType,
            document.FileSizeBytes, document.StoragePath, document.CreatedAt);

    public async Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(bool includeInactive, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);

        var query = ScopeAllWarehouses(_db.Warehouses.AsNoTracking(), companyId, businessUnitId);
        if (!includeInactive)
            query = query.Where(w => w.IsActive);
        return await query
            .OrderByDescending(w => w.IsDefault)
            .ThenByDescending(w => w.IsActive)
            .ThenBy(w => w.Name)
            .Select(w => new WarehouseDto(w.Id, w.CompanyId, w.BusinessUnitId, w.Code, w.Name, w.Address, w.IsActive, w.IsDefault))
            .ToListAsync(ct);
    }

    public async Task<Result<WarehouseDto>> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var validation = ValidateWarehouse(request.Code, request.Name, request.Address);
        if (validation != null)
            return Result<WarehouseDto>.Failure(validation);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Warehouses.AnyAsync(w => w.CompanyId == companyId && w.Code == code, ct))
            return Result<WarehouseDto>.Failure("Mã kho đã tồn tại trong công ty.");

        var hasActiveDefault = await ScopeWarehouses(_db.Warehouses, companyId, businessUnitId)
            .AnyAsync(w => w.IsDefault, ct);
        var isActive = request.IsActive;
        var isDefault = isActive && (request.IsDefault || !hasActiveDefault);
        if (isDefault)
            await ClearDefaultWarehouseAsync(companyId, businessUnitId, null, ct);

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = code,
            Name = request.Name.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            IsActive = isActive,
            IsDefault = isDefault
        };
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct);
        return Result<WarehouseDto>.Success(ToWarehouseDto(warehouse));
    }

    public async Task<Result<WarehouseDto>> UpdateWarehouseAsync(Guid id, UpdateWarehouseRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var validation = ValidateWarehouse(request.Code, request.Name, request.Address);
        if (validation != null)
            return Result<WarehouseDto>.Failure(validation);

        var warehouse = await ScopeAllWarehouses(_db.Warehouses, companyId, businessUnitId)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse == null)
            return Result<WarehouseDto>.Failure("Không tìm thấy kho trong đơn vị Thời trang.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Warehouses.AnyAsync(w => w.CompanyId == companyId && w.Code == code && w.Id != id, ct))
            return Result<WarehouseDto>.Failure("Mã kho đã tồn tại trong công ty.");
        if (warehouse.IsDefault && (!request.IsActive || !request.IsDefault))
            return Result<WarehouseDto>.Failure("Kho mặc định đang được dùng. Hãy chọn một kho khác làm mặc định trước khi ngừng dùng kho này.");
        if (request.IsDefault && !request.IsActive)
            return Result<WarehouseDto>.Failure("Kho mặc định phải đang hoạt động.");

        if (request.IsDefault)
            await ClearDefaultWarehouseAsync(companyId, businessUnitId, id, ct);

        warehouse.Code = code;
        warehouse.Name = request.Name.Trim();
        warehouse.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        warehouse.IsActive = request.IsActive;
        warehouse.IsDefault = request.IsDefault;
        await _db.SaveChangesAsync(ct);
        return Result<WarehouseDto>.Success(ToWarehouseDto(warehouse));
    }

    public async Task<Result<bool>> DeleteWarehouseAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var warehouse = await ScopeAllWarehouses(_db.Warehouses, companyId, businessUnitId)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse == null)
            return Result<bool>.Failure("Không tìm thấy kho trong đơn vị Thời trang.");
        if (warehouse.IsActive && warehouse.IsDefault)
            return Result<bool>.Failure("Không thể xóa kho mặc định. Hãy chọn kho mặc định khác trước.");

        var hasHistory = await _db.InventoryBalances.AnyAsync(b => b.WarehouseId == id, ct) ||
                         await _db.InventoryMovements.AnyAsync(m => m.WarehouseId == id, ct) ||
                         await _db.PurchaseReceipts.AnyAsync(r => r.WarehouseId == id, ct);
        if (hasHistory)
            return Result<bool>.Failure("Kho đã có tồn kho hoặc chứng từ. Không thể xóa để bảo toàn lịch sử; hãy dùng Sửa để ngừng dùng.");

        _db.Warehouses.Remove(warehouse);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private async Task ClearDefaultWarehouseAsync(Guid companyId, Guid? businessUnitId, Guid? exceptId, CancellationToken ct)
    {
        var defaults = await ScopeWarehouses(_db.Warehouses, companyId, businessUnitId)
            .Where(w => w.IsDefault && (!exceptId.HasValue || w.Id != exceptId.Value))
            .ToListAsync(ct);
        foreach (var warehouse in defaults)
            warehouse.IsDefault = false;
    }

    private static string? ValidateWarehouse(string? code, string? name, string? address)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 100)
            return "Mã kho là bắt buộc và tối đa 100 ký tự.";
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return "Tên kho là bắt buộc và tối đa 200 ký tự.";
        if (!string.IsNullOrWhiteSpace(address) && address.Trim().Length > 500)
            return "Địa chỉ kho tối đa 500 ký tự.";
        return null;
    }

    private static WarehouseDto ToWarehouseDto(Warehouse warehouse) => new(
        warehouse.Id, warehouse.CompanyId, warehouse.BusinessUnitId, warehouse.Code,
        warehouse.Name, warehouse.Address, warehouse.IsActive, warehouse.IsDefault);

    public async Task<PaginatedResult<InventoryBalanceDto>> GetBalancesAsync(string? search, Guid? warehouseId, int pageIndex, int pageSize, CancellationToken ct)
    {
        (pageIndex, pageSize) = NormalizePaging(pageIndex, pageSize);
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var warehouse = await GetWarehouseForReadAsync(companyId, businessUnitId, warehouseId, ct);
        if (warehouse == null)
            return new PaginatedResult<InventoryBalanceDto>(Array.Empty<InventoryBalanceDto>(), 0, pageIndex, pageSize);

        var query = _db.InventoryBalances
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.BusinessUnitId == businessUnitId && b.WarehouseId == warehouse.Id);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(b => b.ProductVariant.Sku.Contains(search) || b.ProductVariant.Product.Name.Contains(search));

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(b => b.LastUpdated)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new InventoryBalanceDto(
                b.Id,
                b.CompanyId,
                b.WarehouseId,
                b.Warehouse.Name,
                b.ProductVariantId,
                b.ProductVariant.Sku,
                b.ProductVariant.Product.Name,
                b.ProductVariant.Color,
                b.ProductVariant.Size,
                b.OnHandQuantity,
                b.ReservedQuantity,
                b.OnHandQuantity - b.ReservedQuantity,
                b.LastUpdated))
            .ToListAsync(ct);

        return new PaginatedResult<InventoryBalanceDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<PaginatedResult<InventoryMovementDto>> GetMovementsAsync(Guid? variantId, Guid? warehouseId, int pageIndex, int pageSize, CancellationToken ct)
    {
        (pageIndex, pageSize) = NormalizePaging(pageIndex, pageSize);
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var warehouse = await GetWarehouseForReadAsync(companyId, businessUnitId, warehouseId, ct);
        if (warehouse == null)
            return new PaginatedResult<InventoryMovementDto>(Array.Empty<InventoryMovementDto>(), 0, pageIndex, pageSize);

        var query = _db.InventoryMovements
            .AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.BusinessUnitId == businessUnitId && m.WarehouseId == warehouse.Id);

        if (variantId.HasValue)
            query = query.Where(m => m.ProductVariantId == variantId.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.MovementDate)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new InventoryMovementDto(
                m.Id,
                m.CompanyId,
                m.WarehouseId,
                m.Warehouse.Name,
                m.ProductVariantId,
                m.ProductVariant.Sku,
                m.ProductVariant.Product.Name,
                m.MovementType,
                m.QuantityDelta,
                m.UnitCost,
                m.ReferenceType,
                m.ReferenceId,
                m.MovementDate,
                m.Notes))
            .ToListAsync(ct);

        return new PaginatedResult<InventoryMovementDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<PaginatedResult<PurchaseReceiptDto>> GetPurchaseReceiptsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        (pageIndex, pageSize) = NormalizePaging(pageIndex, pageSize);
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var query = _db.PurchaseReceipts
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.BusinessUnitId == businessUnitId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.ReceiptNumber.Contains(search) || r.Supplier.Name.Contains(search));

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new PurchaseReceiptDto(
                r.Id,
                r.CompanyId,
                r.WarehouseId,
                r.Warehouse.Name,
                r.ReceiptNumber,
                r.SupplierId,
                r.Supplier.Name,
                r.TotalAmount,
                r.ReceivedAt,
                r.Status,
                r.Notes,
                r.Items.Count,
                r.CreatedAt,
                null,
                null,
                null,
                null,
                null))
            .ToListAsync(ct);

        var attachments = await GetLatestReceiptAttachmentsAsync(
            companyId, businessUnitId, items.Select(item => item.Id).ToArray(), ct);
        items = items
            .Select(item => attachments.TryGetValue(item.Id, out var attachment)
                ? item with
                {
                    AttachmentId = attachment.Id,
                    AttachmentFileName = attachment.FileName,
                    AttachmentContentType = attachment.ContentType,
                    AttachmentSizeBytes = attachment.FileSizeBytes,
                    AttachmentUrl = attachment.StoragePath
                }
                : item)
            .ToList();

        return new PaginatedResult<PurchaseReceiptDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<Result<PurchaseReceiptDto>> CreatePurchaseReceiptAsync(CreatePurchaseReceiptRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(
            s => s.Id == request.SupplierId && s.CompanyId == companyId && s.BusinessUnitId == businessUnitId, ct);
        if (supplier == null)
            return Result<PurchaseReceiptDto>.Failure("Nhà cung cấp không tồn tại trong đơn vị Thời trang.");

        var supplierParty = _partyResolver is null
            ? null
            : await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                companyId,
                businessUnitId,
                PartyType.Organization,
                PartyRole.Supplier,
                supplier.Name,
                supplier.Email,
                supplier.Phone,
                "FASHION_SUPPLIER",
                supplier.Code), ct);
        if (supplierParty is not null)
            supplier.PartyId = supplierParty.Id;
        if (request.Items == null || request.Items.Count == 0)
            return Result<PurchaseReceiptDto>.Failure("Phiếu nhập kho phải có ít nhất một mặt hàng.");
        if (request.Items.GroupBy(x => x.ProductVariantId).Any(group => group.Count() > 1))
            return Result<PurchaseReceiptDto>.Failure("Mỗi SKU chỉ được xuất hiện một lần trong một phiếu nhập.");

        var validatedItems = new List<(CreatePurchaseReceiptItemRequest Request, ProductVariant Variant)>();
        foreach (var itemReq in request.Items)
        {
            var variant = await _db.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == itemReq.ProductVariantId &&
                                           v.Product.CompanyId == companyId &&
                                           v.Product.BusinessUnitId == businessUnitId &&
                                           !v.Product.IsDeleted && v.IsActive, ct);
            if (variant == null)
                return Result<PurchaseReceiptDto>.Failure($"Biến thể sản phẩm {itemReq.ProductVariantId} không tồn tại trong đơn vị Thời trang.");
            if (variant.SourcingType != SourcingType.Buy)
                return Result<PurchaseReceiptDto>.Failure($"SKU {variant.Sku} là hàng MAKE (tự sản xuất), không được lập phiếu nhập thành phẩm. Hãy dùng quy trình sản xuất hoặc chuyển SKU sang BUY.");
            if (itemReq.Quantity <= 0)
                return Result<PurchaseReceiptDto>.Failure($"Số lượng nhập cho SKU {variant.Sku} phải lớn hơn 0.");
            if (itemReq.UnitPrice < 0)
                return Result<PurchaseReceiptDto>.Failure($"Đơn giá nhập cho SKU {variant.Sku} không được âm.");

            validatedItems.Add((itemReq, variant));
        }

        var warehouse = await GetOrCreateWarehouseAsync(companyId, businessUnitId, request.WarehouseId, ct);
        if (warehouse == null)
            return Result<PurchaseReceiptDto>.Failure("Kho nhập không tồn tại, không hoạt động hoặc không thuộc đơn vị Thời trang.");

        var now = DateTime.UtcNow;
        var receiptNumber = $"PR-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..25].ToUpperInvariant();
        var receipt = new PurchaseReceipt
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            SupplierId = request.SupplierId,
            ReceiptNumber = receiptNumber,
            Status = "Completed",
            ReceivedAt = now,
            Notes = request.Notes,
            Items = new List<PurchaseReceiptItem>()
        };

        decimal totalAmount = 0;
        foreach (var (itemReq, variant) in validatedItems)
        {
            var balance = await _db.InventoryBalances.FirstOrDefaultAsync(
                b => b.CompanyId == companyId && b.BusinessUnitId == businessUnitId &&
                     b.WarehouseId == warehouse.Id && b.ProductVariantId == itemReq.ProductVariantId, ct);
            var previousOnHand = balance?.OnHandQuantity ?? 0;
            var previousCost = variant.CostPrice;

            receipt.Items.Add(new PurchaseReceiptItem
            {
                Id = Guid.NewGuid(),
                PurchaseReceiptId = receipt.Id,
                PurchaseReceipt = receipt,
                ProductVariantId = itemReq.ProductVariantId,
                ProductVariant = variant,
                Quantity = itemReq.Quantity,
                UnitPrice = itemReq.UnitPrice
            });
            totalAmount += itemReq.Quantity * itemReq.UnitPrice;

            _db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                ProductVariantId = itemReq.ProductVariantId,
                MovementType = InventoryMovementType.PurchaseReceipt,
                QuantityDelta = itemReq.Quantity,
                UnitCost = itemReq.UnitPrice,
                ReferenceType = "PurchaseReceipt",
                ReferenceId = receipt.Id,
                MovementDate = now,
                Notes = $"Nhập kho theo phiếu {receiptNumber}"
            });

            if (balance == null)
            {
                balance = new InventoryBalance
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    BusinessUnitId = businessUnitId,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse,
                    ProductVariantId = itemReq.ProductVariantId,
                    OnHandQuantity = itemReq.Quantity,
                    ReservedQuantity = 0,
                    LastUpdated = now
                };
                _db.InventoryBalances.Add(balance);
            }
            else
            {
                balance.OnHandQuantity += itemReq.Quantity;
                balance.LastUpdated = now;
            }

            var totalQuantityAfterReceipt = previousOnHand + itemReq.Quantity;
            variant.CostPrice = totalQuantityAfterReceipt == 0
                ? itemReq.UnitPrice
                : ((previousOnHand * previousCost) + (itemReq.Quantity * itemReq.UnitPrice)) / totalQuantityAfterReceipt;
            variant.CostStatus = CostStatus.Actual;
            variant.LastActualCostAt = now;
        }

        receipt.TotalAmount = totalAmount;
        if (_documentRegistry is not null)
        {
            var businessDocument = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId,
                BusinessDocumentType.FashionPurchaseReceipt,
                nameof(PurchaseReceipt),
                receipt.Id,
                receipt.ReceiptNumber,
                receipt.TotalAmount,
                receipt.ReceivedAt,
                supplierParty?.Id ?? supplier.PartyId,
                ExternalSourceSystem: "FASHION_SUPPLIER",
                ExternalSourceId: supplier.Code), ct);
            receipt.BusinessDocumentId = businessDocument.Id;
        }
        _db.PurchaseReceipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        return Result<PurchaseReceiptDto>.Success(new PurchaseReceiptDto(
            receipt.Id, receipt.CompanyId, receipt.WarehouseId, warehouse.Name, receipt.ReceiptNumber,
            receipt.SupplierId, supplier.Name, receipt.TotalAmount, receipt.ReceivedAt, receipt.Status,
            receipt.Notes, receipt.Items.Count, receipt.CreatedAt));
    }

    public async Task<PurchaseReceiptDetailDto?> GetPurchaseReceiptByIdAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var receipt = await _db.PurchaseReceipts
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId && r.BusinessUnitId == businessUnitId, ct);

        if (receipt == null)
            return null;

        var itemsDto = receipt.Items.Select(i => new PurchaseReceiptItemDto(
            i.Id, i.ProductVariantId, i.ProductVariant.Sku, i.ProductVariant.Product.Name,
            i.ProductVariant.Color, i.ProductVariant.Size, i.Quantity, i.UnitPrice,
            i.Quantity * i.UnitPrice)).ToList();

        var attachment = (await GetLatestReceiptAttachmentsAsync(
            companyId, businessUnitId, new[] { receipt.Id }, ct)).GetValueOrDefault(receipt.Id);

        return new PurchaseReceiptDetailDto(
            receipt.Id, receipt.CompanyId, receipt.WarehouseId, receipt.Warehouse?.Name ?? string.Empty,
            receipt.ReceiptNumber, receipt.SupplierId, receipt.Supplier?.Name ?? string.Empty,
            receipt.TotalAmount, receipt.ReceivedAt, receipt.Status, receipt.Notes, receipt.CreatedAt, itemsDto,
            attachment?.Id, attachment?.FileName, attachment?.ContentType, attachment?.FileSizeBytes, attachment?.StoragePath);
    }

    public async Task<Result<PurchaseReceiptDto>> UpdatePurchaseReceiptHeaderAsync(
        Guid id, UpdatePurchaseReceiptHeaderRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var receipt = await _db.PurchaseReceipts
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId && r.BusinessUnitId == businessUnitId, ct);
        if (receipt == null)
            return Result<PurchaseReceiptDto>.Failure("Không tìm thấy phiếu nhập kho trong đơn vị Thời trang.");
        if (string.Equals(receipt.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return Result<PurchaseReceiptDto>.Failure("Phiếu nhập đã hủy chỉ được xem lịch sử, không thể sửa.");

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s =>
            s.Id == request.SupplierId && s.CompanyId == companyId && s.BusinessUnitId == businessUnitId, ct);
        if (supplier == null)
            return Result<PurchaseReceiptDto>.Failure("Nhà cung cấp không tồn tại trong đơn vị Thời trang.");

        var supplierParty = _partyResolver is null
            ? null
            : await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                companyId, businessUnitId, PartyType.Organization, PartyRole.Supplier,
                supplier.Name, supplier.Email, supplier.Phone, "FASHION_SUPPLIER", supplier.Code), ct);
        if (supplierParty is not null)
            supplier.PartyId = supplierParty.Id;

        receipt.SupplierId = supplier.Id;
        receipt.Supplier = supplier;
        receipt.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (receipt.BusinessDocumentId.HasValue)
        {
            var document = await _db.BusinessDocuments.FirstOrDefaultAsync(d => d.Id == receipt.BusinessDocumentId.Value, ct);
            if (document != null)
                document.PartyId = supplierParty?.Id ?? supplier.PartyId;
        }

        await _db.SaveChangesAsync(ct);
        return Result<PurchaseReceiptDto>.Success(ToPurchaseReceiptDto(receipt));
    }

    public async Task<Result<PurchaseReceiptDto>> CancelPurchaseReceiptAsync(
        Guid id, CancelPurchaseReceiptRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var receipt = await _db.PurchaseReceipts
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.Items)
                .ThenInclude(item => item.ProductVariant)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId && r.BusinessUnitId == businessUnitId, ct);
        if (receipt == null)
            return Result<PurchaseReceiptDto>.Failure("Không tìm thấy phiếu nhập kho trong đơn vị Thời trang.");
        if (string.Equals(receipt.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return Result<PurchaseReceiptDto>.Failure("Phiếu nhập này đã được hủy trước đó.");

        var balances = new Dictionary<Guid, InventoryBalance>();
        foreach (var item in receipt.Items)
        {
            var balance = await _db.InventoryBalances.FirstOrDefaultAsync(b =>
                b.CompanyId == companyId && b.BusinessUnitId == businessUnitId &&
                b.WarehouseId == receipt.WarehouseId && b.ProductVariantId == item.ProductVariantId, ct);
            if (balance == null || balance.AvailableQuantity < item.Quantity)
                return Result<PurchaseReceiptDto>.Failure(
                    $"Không thể hủy {receipt.ReceiptNumber}: SKU {item.ProductVariant.Sku} không còn đủ tồn khả dụng để hoàn kho. Hãy xử lý các đơn giữ hàng/giao hàng trước.");
            balances[item.ProductVariantId] = balance;
        }

        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Không nêu lý do" : request.Reason.Trim();
        foreach (var item in receipt.Items)
        {
            var balance = balances[item.ProductVariantId];
            balance.OnHandQuantity -= item.Quantity;
            balance.LastUpdated = now;
            _db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                WarehouseId = receipt.WarehouseId,
                Warehouse = receipt.Warehouse,
                ProductVariantId = item.ProductVariantId,
                ProductVariant = item.ProductVariant,
                MovementType = InventoryMovementType.SupplierReturn,
                QuantityDelta = -item.Quantity,
                UnitCost = item.UnitPrice,
                ReferenceType = "PurchaseReceiptCancellation",
                ReferenceId = receipt.Id,
                MovementDate = now,
                Notes = $"Hủy phiếu nhập {receipt.ReceiptNumber}. Lý do: {reason}"
            });
        }

        receipt.Status = "Cancelled";
        receipt.Notes = string.IsNullOrWhiteSpace(receipt.Notes)
            ? $"[Đã hủy {now:dd/MM/yyyy HH:mm}] {reason}"
            : $"{receipt.Notes}\n[Đã hủy {now:dd/MM/yyyy HH:mm}] {reason}";
        if (receipt.BusinessDocumentId.HasValue)
        {
            var document = await _db.BusinessDocuments.FirstOrDefaultAsync(d => d.Id == receipt.BusinessDocumentId.Value, ct);
            if (document != null)
                document.Status = BusinessDocumentStatus.Voided;
        }

        await _db.SaveChangesAsync(ct);
        return Result<PurchaseReceiptDto>.Success(ToPurchaseReceiptDto(receipt));
    }

    private static PurchaseReceiptDto ToPurchaseReceiptDto(PurchaseReceipt receipt) => new(
        receipt.Id, receipt.CompanyId, receipt.WarehouseId, receipt.Warehouse?.Name ?? string.Empty,
        receipt.ReceiptNumber, receipt.SupplierId, receipt.Supplier?.Name ?? string.Empty,
        receipt.TotalAmount, receipt.ReceivedAt, receipt.Status, receipt.Notes,
        receipt.Items.Count, receipt.CreatedAt);

    public async Task<Result<PurchaseReceiptAttachmentDto>> UploadPurchaseReceiptAttachmentAsync(
        Guid receiptId,
        Stream content,
        string fileName,
        string? contentType,
        long length,
        CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var receipt = await _db.PurchaseReceipts.FirstOrDefaultAsync(
            r => r.Id == receiptId && r.CompanyId == companyId && r.BusinessUnitId == businessUnitId, ct);
        if (receipt == null)
            return Result<PurchaseReceiptAttachmentDto>.Failure("Không tìm thấy phiếu nhập kho trong đúng đơn vị Thời trang.");

        if (_fileStorage == null)
            return Result<PurchaseReceiptAttachmentDto>.Failure("Chưa bật dịch vụ lưu trữ chứng từ trên API.");

        var upload = await _fileStorage.UploadAsync(
            content,
            fileName,
            contentType,
            length,
            $"purchase-receipts/{companyId:N}",
            ct);
        if (!upload.Succeeded || upload.Value == null)
            return Result<PurchaseReceiptAttachmentDto>.Failure(upload.Errors);

        var stored = upload.Value;
        var document = new Document
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            FileName = Path.GetFileName(fileName),
            StoragePath = stored.SecureUrl,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? string.Equals(stored.Format, "pdf", StringComparison.OrdinalIgnoreCase)
                    ? "application/pdf"
                    : "application/octet-stream"
                : contentType,
            FileSizeBytes = stored.Bytes,
            Category = "PurchaseInvoice",
            EntityType = "PurchaseReceipt",
            EntityId = receipt.Id,
            StorageProvider = "Cloudinary",
            StoragePublicId = stored.PublicId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserId?.ToString()
        };
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Stored purchase receipt attachment {DocumentId} for receipt {ReceiptId} via {StorageProvider}.",
            document.Id, receipt.Id, document.StorageProvider);
        return Result<PurchaseReceiptAttachmentDto>.Success(ToAttachmentDto(document, receipt.Id));
    }

    public async Task<Result<InventoryMovementDto>> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var businessUnitId = await GetFashionBusinessUnitIdAsync(companyId, ct);
        var variant = await _db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId &&
                                       v.Product.CompanyId == companyId &&
                                       v.Product.BusinessUnitId == businessUnitId &&
                                       !v.Product.IsDeleted && v.IsActive, ct);
        if (variant == null)
            return Result<InventoryMovementDto>.Failure("Biến thể sản phẩm không tồn tại trong đơn vị Thời trang.");
        if (request.QuantityDelta == 0)
            return Result<InventoryMovementDto>.Failure("Số lượng điều chỉnh phải khác 0.");

        var warehouse = await GetOrCreateWarehouseAsync(companyId, businessUnitId, request.WarehouseId, ct);
        if (warehouse == null)
            return Result<InventoryMovementDto>.Failure("Kho điều chỉnh không tồn tại, không hoạt động hoặc không thuộc đơn vị Thời trang.");

        var balance = await _db.InventoryBalances.FirstOrDefaultAsync(
            b => b.CompanyId == companyId && b.BusinessUnitId == businessUnitId &&
                 b.WarehouseId == warehouse.Id && b.ProductVariantId == request.ProductVariantId, ct);
        if (balance != null)
        {
            if (balance.OnHandQuantity + request.QuantityDelta < balance.ReservedQuantity)
                return Result<InventoryMovementDto>.Failure($"Số lượng khả dụng không đủ để giảm. Tồn kho hiện tại: {balance.OnHandQuantity}, đang giữ: {balance.ReservedQuantity}.");

            balance.OnHandQuantity += request.QuantityDelta;
            balance.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            if (request.QuantityDelta < 0)
                return Result<InventoryMovementDto>.Failure("Không thể giảm tồn kho cho sản phẩm chưa có số dư kho.");

            balance = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                ProductVariantId = request.ProductVariantId,
                OnHandQuantity = request.QuantityDelta,
                ReservedQuantity = 0,
                LastUpdated = DateTime.UtcNow
            };
            _db.InventoryBalances.Add(balance);
        }

        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            ProductVariantId = request.ProductVariantId,
            MovementType = InventoryMovementType.StockAdjustment,
            QuantityDelta = request.QuantityDelta,
            UnitCost = variant.CostPrice,
            MovementDate = DateTime.UtcNow,
            Notes = request.Notes ?? "Điều chỉnh kiểm kê kho thủ công"
        };
        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(ct);

        return Result<InventoryMovementDto>.Success(new InventoryMovementDto(
            movement.Id, movement.CompanyId, movement.WarehouseId, warehouse.Name, movement.ProductVariantId,
            variant.Sku, variant.Product.Name, movement.MovementType, movement.QuantityDelta, movement.UnitCost,
            movement.ReferenceType, movement.ReferenceId, movement.MovementDate, movement.Notes));
    }
}
