using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260825153000_AddFashionPerformanceIndexes")]
public partial class AddFashionPerformanceIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_products_company_bu_created",
            table: "products",
            columns: new[] { "company_id", "business_unit_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_product_variants_product_active_source",
            table: "product_variants",
            columns: new[] { "product_id", "is_active", "sourcing_type" });

        migrationBuilder.CreateIndex(
            name: "ix_suppliers_company_bu_created",
            table: "suppliers",
            columns: new[] { "company_id", "business_unit_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_receipts_company_bu_created",
            table: "purchase_receipts",
            columns: new[] { "company_id", "business_unit_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_inventory_movements_company_bu_warehouse_date",
            table: "inventory_movements",
            columns: new[] { "company_id", "business_unit_id", "warehouse_id", "movement_date" });

        migrationBuilder.CreateIndex(
            name: "ix_inventory_movements_company_bu_warehouse_variant_date",
            table: "inventory_movements",
            columns: new[] { "company_id", "business_unit_id", "warehouse_id", "product_variant_id", "movement_date" });

        migrationBuilder.CreateIndex(
            name: "ix_inventory_balances_company_bu_warehouse_updated",
            table: "inventory_balances",
            columns: new[] { "company_id", "business_unit_id", "warehouse_id", "last_updated" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_products_company_bu_created",
            table: "products");
        migrationBuilder.DropIndex(
            name: "ix_product_variants_product_active_source",
            table: "product_variants");
        migrationBuilder.DropIndex(
            name: "ix_suppliers_company_bu_created",
            table: "suppliers");
        migrationBuilder.DropIndex(
            name: "ix_purchase_receipts_company_bu_created",
            table: "purchase_receipts");
        migrationBuilder.DropIndex(
            name: "ix_inventory_movements_company_bu_warehouse_date",
            table: "inventory_movements");
        migrationBuilder.DropIndex(
            name: "ix_inventory_movements_company_bu_warehouse_variant_date",
            table: "inventory_movements");
        migrationBuilder.DropIndex(
            name: "ix_inventory_balances_company_bu_warehouse_updated",
            table: "inventory_balances");
    }
}
