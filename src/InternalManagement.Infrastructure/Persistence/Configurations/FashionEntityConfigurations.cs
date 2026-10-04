using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class FashionEntityConfigurations :
    IEntityTypeConfiguration<Warehouse>,
    IEntityTypeConfiguration<Product>,
    IEntityTypeConfiguration<ProductVariant>,
    IEntityTypeConfiguration<Supplier>,
    IEntityTypeConfiguration<PurchaseReceipt>,
    IEntityTypeConfiguration<PurchaseReceiptItem>,
    IEntityTypeConfiguration<InventoryMovement>,
    IEntityTypeConfiguration<InventoryBalance>,
    IEntityTypeConfiguration<SalesOrder>,
    IEntityTypeConfiguration<SalesOrderItem>,
    IEntityTypeConfiguration<Return>,
    IEntityTypeConfiguration<ReturnItem>,
    IEntityTypeConfiguration<SalesSettlement>,
    IEntityTypeConfiguration<SalesDocument>,
    IEntityTypeConfiguration<SalesDocumentItem>
{
    // Fashion & Inventory
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => new { w.CompanyId, w.Code }).IsUnique();
        builder.HasIndex(w => new { w.CompanyId, w.BusinessUnitId, w.IsDefault })
            .IsUnique()
            .HasFilter("is_default = true AND is_active = true");
        builder.Property(w => w.Code).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Address).HasMaxLength(500);
    }

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
        builder.HasIndex(p => new { p.CompanyId, p.BusinessUnitId, p.CreatedAt })
            .HasDatabaseName("ix_products_company_bu_created");
        builder.Property(p => p.Code).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(300).IsRequired();
        builder.HasQueryFilter(p => !p.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => v.Sku).IsUnique();
        builder.HasIndex(v => new { v.ProductId, v.IsActive, v.SourcingType })
            .HasDatabaseName("ix_product_variants_product_active_source");
        builder.Property(v => v.Sku).HasMaxLength(100).IsRequired();
        builder.Property(v => v.SourcingType).HasConversion<string>();
        builder.Property(v => v.CostPrice).HasPrecision(18, 2);
        builder.Property(v => v.CostStatus).HasConversion<string>();
        builder.Property(v => v.SellingPrice).HasPrecision(18, 2);
        builder.HasOne(v => v.Product).WithMany(p => p.Variants).HasForeignKey(v => v.ProductId);
        builder.HasQueryFilter(v => !v.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();
        builder.HasIndex(s => new { s.CompanyId, s.BusinessUnitId, s.CreatedAt })
            .HasDatabaseName("ix_suppliers_company_bu_created");
        builder.Property(s => s.Code).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.PhoneNumbers).HasMaxLength(4000);
        builder.Property(s => s.BankAccounts).HasMaxLength(4000);
    }

    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyId, r.ReceiptNumber }).IsUnique();
        builder.HasIndex(r => new { r.CompanyId, r.BusinessUnitId, r.CreatedAt })
            .HasDatabaseName("ix_purchase_receipts_company_bu_created");
        builder.Property(r => r.ReceiptNumber).HasMaxLength(100).IsRequired();
        builder.Property(r => r.TotalAmount).HasPrecision(18, 2);
        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId);
        builder.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<PurchaseReceiptItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.HasOne(i => i.PurchaseReceipt).WithMany(r => r.Items).HasForeignKey(i => i.PurchaseReceiptId);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId);
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.CompanyId, m.WarehouseId, m.MovementDate });
        builder.HasIndex(m => new { m.CompanyId, m.BusinessUnitId, m.WarehouseId, m.MovementDate })
            .HasDatabaseName("ix_inventory_movements_company_bu_warehouse_date");
        builder.HasIndex(m => new { m.CompanyId, m.BusinessUnitId, m.WarehouseId, m.ProductVariantId, m.MovementDate })
            .HasDatabaseName("ix_inventory_movements_company_bu_warehouse_variant_date");
        builder.Property(m => m.MovementType).HasConversion<string>();
        builder.Property(m => m.UnitCost).HasPrecision(18, 2);
        builder.HasOne(m => m.ProductVariant).WithMany().HasForeignKey(m => m.ProductVariantId);
        builder.HasOne(m => m.Warehouse).WithMany(w => w.Movements).HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(m => !m.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.CompanyId, b.WarehouseId, b.ProductVariantId }).IsUnique();
        builder.HasIndex(b => new { b.CompanyId, b.BusinessUnitId, b.WarehouseId, b.LastUpdated })
            .HasDatabaseName("ix_inventory_balances_company_bu_warehouse_updated");
        builder.Property(b => b.Version).IsRowVersion();
        builder.HasOne(b => b.ProductVariant).WithMany(v => v.Balances).HasForeignKey(b => b.ProductVariantId);
        builder.HasOne(b => b.Warehouse).WithMany(w => w.Balances).HasForeignKey(b => b.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(b => !b.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => new { o.CompanyId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.CompanyId, o.SourceSystem, o.SourceOrderId })
            .IsUnique()
            .HasFilter("source_order_id IS NOT NULL");
        builder.Property(o => o.OrderNumber).HasMaxLength(100).IsRequired();
        foreach (var property in new[] { nameof(SalesOrder.TotalAmount), nameof(SalesOrder.GrossAmount), nameof(SalesOrder.DiscountAmount), nameof(SalesOrder.ShippingCustomerPaid), nameof(SalesOrder.ShippingShopSubsidy), nameof(SalesOrder.TaxAmount), nameof(SalesOrder.PlatformFee), nameof(SalesOrder.AffiliateFee), nameof(SalesOrder.PaymentFee), nameof(SalesOrder.AdvertisingCost), nameof(SalesOrder.PackagingCost), nameof(SalesOrder.OtherSellingExpense), nameof(SalesOrder.ActualCogs), nameof(SalesOrder.NetRevenue), nameof(SalesOrder.Profit) })
        {
            builder.Property<decimal>(property).HasPrecision(18, 6);
        }
        builder.Property(o => o.SourceSystem).HasMaxLength(30).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>();
        builder.Property(o => o.CostStatus).HasConversion<string>();
        builder.HasOne(o => o.Warehouse).WithMany().HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.UnitCostSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ProductNameSnapshot).HasMaxLength(300).IsRequired();
        builder.HasOne(i => i.SalesOrder).WithMany(o => o.Items).HasForeignKey(i => i.SalesOrderId);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId);
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Return> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyId, r.ReturnNumber }).IsUnique();
        builder.Property(r => r.ReturnNumber).HasMaxLength(100).IsRequired();
        builder.Property(r => r.TotalRefundAmount).HasPrecision(18, 2);
        builder.HasOne(r => r.SalesOrder).WithMany(o => o.Returns).HasForeignKey(r => r.SalesOrderId);
        builder.HasOne(r => r.BusinessDocument).WithMany().HasForeignKey(r => r.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<ReturnItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.UnitCostSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.RefundAmount).HasPrecision(18, 2);
        builder.HasOne(i => i.Return).WithMany(r => r.Items).HasForeignKey(i => i.ReturnId);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId);
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<SalesSettlement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.PaymentReference }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.Status, x.OccurredAt });
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentReference).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(100);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne(x => x.SalesOrder).WithMany(x => x.Settlements).HasForeignKey(x => x.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesDocument).WithMany().HasForeignKey(x => x.SalesDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Return).WithMany().HasForeignKey(x => x.ReturnId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<SalesDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.DocumentType }).IsUnique();
        builder.Property(x => x.DocumentNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>();
        foreach (var property in new[] { nameof(SalesDocument.GrossAmount), nameof(SalesDocument.DiscountAmount), nameof(SalesDocument.TaxAmount), nameof(SalesDocument.TotalAmount) })
        {
            builder.Property<decimal>(property).HasPrecision(18, 6);
        }
        builder.HasOne(x => x.SalesOrder).WithMany(x => x.Documents).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SalesDocumentItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SkuSnapshot).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(300).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(18, 6);
        builder.Property(x => x.LineTotal).HasPrecision(18, 6);
        builder.HasOne(x => x.SalesDocument).WithMany(x => x.Items).HasForeignKey(x => x.SalesDocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.ProductVariant.Product.IsDeleted);
    }

}
