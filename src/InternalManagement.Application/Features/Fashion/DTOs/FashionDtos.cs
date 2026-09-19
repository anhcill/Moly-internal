using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Fashion.DTOs;

// ── Product DTOs ──

public record ProductDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Code,
    string Name,
    string? Category,
    string? Description,
    bool IsActive,
    int VariantCount,
    DateTime CreatedAt);

public record ProductDetailDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Code,
    string Name,
    string? Category,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    IReadOnlyList<ProductVariantDto> Variants);

public record ProductVariantDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    string? Barcode,
    string? Color,
    string? Size,
    decimal CostPrice,
    decimal SellingPrice,
    bool IsActive,
    int OnHandQuantity,
    DateTime CreatedAt,
    SourcingType SourcingType,
    CostStatus CostStatus,
    DateTime? LastActualCostAt)
{
    public string SourcingTypeLabel => SourcingType switch
    {
        SourcingType.Make => "Tự sản xuất",
        SourcingType.Buy => "Mua ngoài",
        _ => "Chưa xác định"
    };

    public string CostStatusLabel => CostStatus switch
    {
        CostStatus.Standard => "Giá chuẩn",
        CostStatus.Estimated => "Giá ước tính",
        CostStatus.Actual => "Giá thực tế",
        CostStatus.Provisional => "Giá tạm tính",
        _ => "Chưa xác định"
    };
}

public record CreateProductRequest(
    string Code,
    string Name,
    string? Category,
    string? Description);

public record UpdateProductRequest(
    string Code,
    string Name,
    string? Category,
    string? Description,
    bool IsActive = true);

public record CreateProductVariantRequest(
    string Sku,
    string? Barcode,
    string? Color,
    string? Size,
    decimal CostPrice,
    decimal SellingPrice,
    SourcingType SourcingType = SourcingType.Make);

public record UpdateProductVariantRequest(
    string Sku,
    string? Barcode,
    string? Color,
    string? Size,
    decimal CostPrice,
    decimal SellingPrice,
    SourcingType SourcingType,
    bool IsActive = true);

// ── Supplier DTOs ──

public record SupplierDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? PhoneNumbers,
    string? Email,
    string? Address,
    string? BankAccounts,
    DateTime CreatedAt);

public record CreateSupplierRequest(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? PhoneNumbers = null,
    string? BankAccounts = null);

public record UpdateSupplierRequest(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? PhoneNumbers = null,
    string? BankAccounts = null);

public record WarehouseDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Code,
    string Name,
    string? Address,
    bool IsActive,
    bool IsDefault);

public record CreateWarehouseRequest(
    string Code,
    string Name,
    string? Address,
    bool IsActive = true,
    bool IsDefault = false);

public record UpdateWarehouseRequest(
    string Code,
    string Name,
    string? Address,
    bool IsActive,
    bool IsDefault);

// ── Purchase Receipt DTOs ──

public record PurchaseReceiptDto(
    Guid Id,
    Guid CompanyId,
    Guid WarehouseId,
    string WarehouseName,
    string ReceiptNumber,
    Guid SupplierId,
    string SupplierName,
    decimal TotalAmount,
    DateTime ReceivedAt,
    string Status,
    string? Notes,
    int ItemCount,
    DateTime CreatedAt,
    Guid? AttachmentId = null,
    string? AttachmentFileName = null,
    string? AttachmentContentType = null,
    long? AttachmentSizeBytes = null,
    string? AttachmentUrl = null);

public record PurchaseReceiptDetailDto(
    Guid Id,
    Guid CompanyId,
    Guid WarehouseId,
    string WarehouseName,
    string ReceiptNumber,
    Guid SupplierId,
    string SupplierName,
    decimal TotalAmount,
    DateTime ReceivedAt,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<PurchaseReceiptItemDto> Items,
    Guid? AttachmentId = null,
    string? AttachmentFileName = null,
    string? AttachmentContentType = null,
    long? AttachmentSizeBytes = null,
    string? AttachmentUrl = null);

public record PurchaseReceiptAttachmentDto(
    Guid Id,
    Guid ReceiptId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Url,
    DateTime CreatedAt);

public record PurchaseReceiptItemDto(
    Guid Id,
    Guid ProductVariantId,
    string Sku,
    string? ProductName,
    string? Color,
    string? Size,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice);

public record CreatePurchaseReceiptRequest(
    Guid SupplierId,
    string? Notes,
    IReadOnlyList<CreatePurchaseReceiptItemRequest> Items,
    Guid? WarehouseId = null);

public record CreatePurchaseReceiptItemRequest(
    Guid ProductVariantId,
    int Quantity,
    decimal UnitPrice);

/// <summary>
/// Only header information is editable after a receipt has affected stock.
/// Quantities and prices must be corrected by cancelling/reversing the receipt
/// and creating a new one, so the inventory ledger remains auditable.
/// </summary>
public record UpdatePurchaseReceiptHeaderRequest(
    Guid SupplierId,
    string? Notes);

public record CancelPurchaseReceiptRequest(string? Reason);

// ── Inventory DTOs ──

public record InventoryBalanceDto(
    Guid Id,
    Guid CompanyId,
    Guid WarehouseId,
    string WarehouseName,
    Guid ProductVariantId,
    string Sku,
    string? ProductName,
    string? Color,
    string? Size,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    DateTime LastUpdated);

public record InventoryMovementDto(
    Guid Id,
    Guid CompanyId,
    Guid WarehouseId,
    string WarehouseName,
    Guid ProductVariantId,
    string Sku,
    string? ProductName,
    InventoryMovementType MovementType,
    int QuantityDelta,
    decimal UnitCost,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime MovementDate,
    string? Notes)
{
    public string MovementTypeLabel => MovementType switch
    {
        InventoryMovementType.PurchaseReceipt => "Nhập hàng",
        InventoryMovementType.SalesOrderDelivery => "Xuất giao hàng",
        InventoryMovementType.CustomerReturn => "Khách trả hàng",
        InventoryMovementType.SupplierReturn => "Trả nhà cung cấp",
        InventoryMovementType.StockAdjustment => "Điều chỉnh tồn kho",
        InventoryMovementType.Scrap => "Hàng hỏng / loại bỏ",
        InventoryMovementType.ProductionOutput => "Nhập thành phẩm sản xuất",
        InventoryMovementType.MaterialConsumption => "Xuất nguyên vật liệu sản xuất",
        InventoryMovementType.ProductionScrap => "Hao hụt sản xuất",
        _ => "Khác"
    };
}

public record StockAdjustmentRequest(
    Guid ProductVariantId,
    int QuantityDelta,
    string? Notes,
    Guid? WarehouseId = null);
