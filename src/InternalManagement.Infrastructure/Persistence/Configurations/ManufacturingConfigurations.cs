using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.Fashion;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class ManufacturingConfigurations :
    IEntityTypeConfiguration<Material>,
    IEntityTypeConfiguration<MaterialLot>,
    IEntityTypeConfiguration<MaterialMovement>,
    IEntityTypeConfiguration<Bom>,
    IEntityTypeConfiguration<BomItem>,
    IEntityTypeConfiguration<ProductionOrder>,
    IEntityTypeConfiguration<ProductionOrderOutput>,
    IEntityTypeConfiguration<ProductionOrderMaterial>,
    IEntityTypeConfiguration<ProductionOperation>,
    IEntityTypeConfiguration<ChannelFeePolicy>,
    IEntityTypeConfiguration<OrderCostSnapshot>,
    IEntityTypeConfiguration<OrderCostSnapshotItem>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        builder.Property(x => x.QuantityOnHand).HasPrecision(18, 4);
    }

    public void Configure(EntityTypeBuilder<MaterialLot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.LotNumber }).IsUnique();
        builder.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.QuantityReceived).HasPrecision(18, 4);
        builder.Property(x => x.QuantityRemaining).HasPrecision(18, 4);
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.HasOne(x => x.Material).WithMany(x => x.Lots).HasForeignKey(x => x.MaterialId);
        builder.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<MaterialMovement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.MaterialId, x.MovementDate });
        builder.Property(x => x.MovementType).HasConversion<string>();
        builder.Property(x => x.QuantityDelta).HasPrecision(18, 4);
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.HasOne(x => x.Material).WithMany(x => x.Movements).HasForeignKey(x => x.MaterialId);
        builder.HasOne(x => x.MaterialLot).WithMany().HasForeignKey(x => x.MaterialLotId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<Bom> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.ProductId, x.VersionNumber }).IsUnique();
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        builder.HasQueryFilter(x => !x.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<BomItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Size).HasMaxLength(30);
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.WastePercent).HasPrecision(9, 4);
        builder.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        builder.HasOne(x => x.Bom).WithMany(x => x.Items).HasForeignKey(x => x.BomId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.Bom.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        builder.Property(x => x.OrderNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.CostStatus).HasConversion<string>();
        builder.Property(x => x.StandardCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualMaterialCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualLaborCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualOutsideProcessingCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualOverheadCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualScrapReworkCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualTotalCost).HasPrecision(18, 2);
        builder.Property(x => x.ActualUnitCost).HasPrecision(18, 2);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        builder.HasOne(x => x.Bom).WithMany(x => x.ProductionOrders).HasForeignKey(x => x.BomId);
        builder.HasQueryFilter(x => !x.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ProductionOrderOutput> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProductionOrderId, x.ProductVariantId }).IsUnique();
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.HasOne(x => x.ProductionOrder).WithMany(x => x.Outputs).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.ProductionOrder.Product.IsDeleted && !x.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ProductionOrderMaterial> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Size).HasMaxLength(30);
        builder.Property(x => x.PlannedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.ActualQuantity).HasPrecision(18, 4);
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.Property(x => x.TotalCost).HasPrecision(18, 2);
        builder.HasOne(x => x.ProductionOrder).WithMany(x => x.Materials).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MaterialLot).WithMany().HasForeignKey(x => x.MaterialLotId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(x => !x.ProductionOrder.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ProductionOperation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProductionOrderId, x.Sequence }).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RateType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Rate).HasPrecision(18, 2);
        builder.Property(x => x.ActualUnits).HasPrecision(18, 4);
        builder.Property(x => x.TotalCost).HasPrecision(18, 2);
        builder.HasOne(x => x.ProductionOrder).WithMany(x => x.Operations).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(x => !x.ProductionOrder.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ChannelFeePolicy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.Channel, x.VersionNumber }).IsUnique();
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Channel).HasMaxLength(30).IsRequired();
        builder.Property(x => x.PlatformFeeRate).HasPrecision(9, 6);
        builder.Property(x => x.AffiliateFeeRate).HasPrecision(9, 6);
        builder.Property(x => x.PaymentFeeRate).HasPrecision(9, 6);
        builder.Property(x => x.FixedPaymentFee).HasPrecision(18, 2);
        builder.Property(x => x.TaxRate).HasPrecision(9, 6);
        builder.Property(x => x.DefaultShippingSubsidy).HasPrecision(18, 2);
    }

    public void Configure(EntityTypeBuilder<OrderCostSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.SnapshottedAt });
        builder.Property(x => x.Channel).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CostStatus).HasConversion<string>();
        foreach (var property in new[] { nameof(OrderCostSnapshot.GrossAmount), nameof(OrderCostSnapshot.DiscountAmount), nameof(OrderCostSnapshot.RefundAmount), nameof(OrderCostSnapshot.NetSalesAmount), nameof(OrderCostSnapshot.PlatformFee), nameof(OrderCostSnapshot.AffiliateFee), nameof(OrderCostSnapshot.PaymentFee), nameof(OrderCostSnapshot.AdvertisingCost), nameof(OrderCostSnapshot.PackagingCost), nameof(OrderCostSnapshot.OtherSellingExpense), nameof(OrderCostSnapshot.ShippingSubsidy), nameof(OrderCostSnapshot.TaxAmount), nameof(OrderCostSnapshot.ActualCogs), nameof(OrderCostSnapshot.Profit), nameof(OrderCostSnapshot.Margin) })
        {
            builder.Property<decimal>(property).HasPrecision(18, 6);
        }
        builder.HasOne(x => x.SalesOrder).WithMany(x => x.CostSnapshots).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<OrderCostSnapshotItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UnitSellingPrice).HasPrecision(18, 2);
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.Property(x => x.TotalCogs).HasPrecision(18, 2);
        builder.Property(x => x.CostStatus).HasConversion<string>();
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Items).HasForeignKey(x => x.OrderCostSnapshotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.ProductVariant.Product.IsDeleted);
    }
}
