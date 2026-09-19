using InternalManagement.Domain.Common;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Domain.Entities.Fashion;

public class Material : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = "piece";
    public decimal QuantityOnHand { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<MaterialLot> Lots { get; set; } = new List<MaterialLot>();
    public ICollection<MaterialMovement> Movements { get; set; } = new List<MaterialMovement>();
}

public class MaterialLot : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string LotNumber { get; set; } = string.Empty;
    public decimal QuantityReceived { get; set; }
    public decimal QuantityRemaining { get; set; }
    public decimal UnitCost { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class MaterialMovement : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public Guid? MaterialLotId { get; set; }
    public MaterialLot? MaterialLot { get; set; }
    public MaterialMovementType MovementType { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal UnitCost { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public DateTime MovementDate { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public class Bom : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<BomItem> Items { get; set; } = new List<BomItem>();
    public ICollection<ProductionOrder> ProductionOrders { get; set; } = new List<ProductionOrder>();
}

public class BomItem : BaseEntity
{
    public Guid BomId { get; set; }
    public Bom Bom { get; set; } = null!;
    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public string? Size { get; set; }
    public decimal Quantity { get; set; }
    public decimal WastePercent { get; set; }
    public string Unit { get; set; } = "piece";
    public int Sequence { get; set; }
    public string? Notes { get; set; }
}

public class ProductionOrder : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid BomId { get; set; }
    public Bom Bom { get; set; } = null!;
    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Draft;
    public CostStatus CostStatus { get; set; } = CostStatus.Standard;
    public int PlannedQuantity { get; set; }
    public int GoodQuantity { get; set; }
    public int DefectiveQuantity { get; set; }
    public int ReworkQuantity { get; set; }
    public decimal StandardCost { get; set; }
    public decimal ActualMaterialCost { get; set; }
    public decimal ActualLaborCost { get; set; }
    public decimal ActualOutsideProcessingCost { get; set; }
    public decimal ActualOverheadCost { get; set; }
    public decimal ActualScrapReworkCost { get; set; }
    public decimal ActualTotalCost { get; set; }
    public decimal ActualUnitCost { get; set; }
    public DateTime ReleasedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<ProductionOrderOutput> Outputs { get; set; } = new List<ProductionOrderOutput>();
    public ICollection<ProductionOrderMaterial> Materials { get; set; } = new List<ProductionOrderMaterial>();
    public ICollection<ProductionOperation> Operations { get; set; } = new List<ProductionOperation>();
}

public class ProductionOrderOutput : BaseEntity
{
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int PlannedQuantity { get; set; }
    public int GoodQuantity { get; set; }
    public int DefectiveQuantity { get; set; }
    public int ReworkQuantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class ProductionOrderMaterial : BaseEntity
{
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public Guid? MaterialLotId { get; set; }
    public MaterialLot? MaterialLot { get; set; }
    public string? Size { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

public class ProductionOperation : BaseEntity
{
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOutsideProcessing { get; set; }
    public string RateType { get; set; } = "PerPiece";
    public decimal Rate { get; set; }
    public decimal ActualUnits { get; set; }
    public decimal TotalCost { get; set; }
    public string? Notes { get; set; }
}

public class ChannelFeePolicy : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Channel { get; set; } = "STORE";
    public int VersionNumber { get; set; }
    public decimal PlatformFeeRate { get; set; }
    public decimal AffiliateFeeRate { get; set; }
    public decimal PaymentFeeRate { get; set; }
    public decimal FixedPaymentFee { get; set; }
    public decimal TaxRate { get; set; }
    public decimal DefaultShippingSubsidy { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class OrderCostSnapshot : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;
    public string Channel { get; set; } = "STORE";
    public int? ChannelFeePolicyVersion { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal NetSalesAmount { get; set; }
    public decimal PlatformFee { get; set; }
    public decimal AffiliateFee { get; set; }
    public decimal PaymentFee { get; set; }
    public decimal AdvertisingCost { get; set; }
    public decimal PackagingCost { get; set; }
    public decimal OtherSellingExpense { get; set; }
    public decimal ShippingSubsidy { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ActualCogs { get; set; }
    public decimal Profit { get; set; }
    public decimal Margin { get; set; }
    public CostStatus CostStatus { get; set; } = CostStatus.Actual;
    public DateTime SnapshottedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<OrderCostSnapshotItem> Items { get; set; } = new List<OrderCostSnapshotItem>();
}

public class OrderCostSnapshotItem : BaseEntity
{
    public Guid OrderCostSnapshotId { get; set; }
    public OrderCostSnapshot Snapshot { get; set; } = null!;
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitSellingPrice { get; set; }
    public decimal UnitCost { get; set; }
    public CostStatus CostStatus { get; set; }
    public decimal TotalCogs { get; set; }
}
