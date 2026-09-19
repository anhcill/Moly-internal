using InternalManagement.Domain.Common;
using InternalManagement.Domain.Enums;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Entities.Documents;

namespace InternalManagement.Domain.Entities.Fashion;

public class Warehouse : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<InventoryBalance> Balances { get; set; } = new List<InventoryBalance>();
    public ICollection<InventoryMovement> Movements { get; set; } = new List<InventoryMovement>();
}

public class Product : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}

public class ProductVariant : BaseEntity, IAuditableEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public SourcingType SourcingType { get; set; } = SourcingType.Make;
    public decimal CostPrice { get; set; }
    public CostStatus CostStatus { get; set; } = CostStatus.Standard;
    public DateTime? LastActualCostAt { get; set; }
    public decimal SellingPrice { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<InventoryBalance> Balances { get; set; } = new List<InventoryBalance>();
}

public class Supplier : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    /// <summary>One phone number per line. Phone remains the legacy primary number.</summary>
    public string? PhoneNumbers { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    /// <summary>One bank-account description per line, for example: Vietcombank - 0123456789 - Nguyen Van A.</summary>
    public string? BankAccounts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class PurchaseReceipt : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public decimal TotalAmount { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Completed"; // Completed, Cancelled
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<PurchaseReceiptItem> Items { get; set; } = new List<PurchaseReceiptItem>();
}

public class PurchaseReceiptItem : BaseEntity
{
    public Guid PurchaseReceiptId { get; set; }
    public PurchaseReceipt PurchaseReceipt { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice => Quantity * UnitPrice;
}

public class InventoryMovement : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public InventoryMovementType MovementType { get; set; }
    public int QuantityDelta { get; set; } // Positive for increase, negative for decrease
    public decimal UnitCost { get; set; }
    public string? ReferenceType { get; set; } // PurchaseReceipt, SalesOrder, Return, Adjustment
    public Guid? ReferenceId { get; set; }
    public DateTime MovementDate { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public class InventoryBalance : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int OnHandQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public uint Version { get; set; } // Concurrency token
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class SalesOrder : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    /// <summary>Kho đã giữ và xuất hàng cho đơn. Đơn cũ có thể chưa có giá trị này.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Guid? CustomerPartyId { get; set; }
    public Party? CustomerParty { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = "MANUAL"; // MANUAL, SHOPEE, TIKTOK, WEBSITE
    public string? SourceOrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingCustomerPaid { get; set; }
    public decimal ShippingShopSubsidy { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal PlatformFee { get; set; }
    public decimal AffiliateFee { get; set; }
    public decimal PaymentFee { get; set; }
    public decimal AdvertisingCost { get; set; }
    public decimal PackagingCost { get; set; }
    public decimal OtherSellingExpense { get; set; }
    public decimal ActualCogs { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal Profit { get; set; }
    public CostStatus CostStatus { get; set; } = CostStatus.Provisional;
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Pending;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
    public ICollection<Return> Returns { get; set; } = new List<Return>();
    public ICollection<SalesSettlement> Settlements { get; set; } = new List<SalesSettlement>();
    public ICollection<OrderCostSnapshot> CostSnapshots { get; set; } = new List<OrderCostSnapshot>();
    public ICollection<SalesDocument> Documents { get; set; } = new List<SalesDocument>();
}

public class SalesOrderItem : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public int DeliveredQuantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCostSnapshot { get; set; }
    public CostStatus CostStatus { get; set; } = CostStatus.Provisional;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal TotalPrice => Quantity * UnitPrice;
}

public class Return : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public string ReturnNumber { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public decimal TotalRefundAmount { get; set; }
    public string Status { get; set; } = "Inspecting"; // Inspecting, Approved, Rejected, Refunded
    public DateTime ReturnedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();
}

/// <summary>
/// Explicit customer payment or refund confirmation for a Fashion order.  This
/// record—not an order or return status—is the source event for finance.
/// </summary>
public class SalesSettlement : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;
    public Guid? SalesDocumentId { get; set; }
    public SalesDocument? SalesDocument { get; set; }
    public Guid? ReturnId { get; set; }
    public Return? Return { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public SalesSettlementKind Kind { get; set; }
    public SalesSettlementStatus Status { get; set; } = SalesSettlementStatus.Pending;
    public string PaymentReference { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ReturnItem : BaseEntity
{
    public Guid ReturnId { get; set; }
    public Return Return { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public string ConditionStatus { get; set; } = "Good"; // Good, Damaged, Defective
    public bool Restockable { get; set; } = true;
    public decimal UnitPriceSnapshot { get; set; }
    public decimal UnitCostSnapshot { get; set; }
    public decimal RefundAmount { get; set; }
}

/// <summary>
/// Immutable sales invoice/retail receipt header. It deliberately stores snapshots
/// instead of references to mutable product or order pricing data.
/// </summary>
public class SalesDocument : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? CustomerPartyId { get; set; }
    public Party? CustomerParty { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;
    public SalesDocumentType DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<SalesDocumentItem> Items { get; set; } = new List<SalesDocumentItem>();
}

public class SalesDocumentItem : BaseEntity
{
    public Guid SalesDocumentId { get; set; }
    public SalesDocument SalesDocument { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string? ColorSnapshot { get; set; }
    public string? SizeSnapshot { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
