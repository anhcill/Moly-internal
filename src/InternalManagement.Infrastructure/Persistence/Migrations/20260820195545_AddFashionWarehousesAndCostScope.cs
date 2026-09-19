using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFashionWarehousesAndCostScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_company_id_movement_date",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "ix_inventory_balances_company_id_product_variant_id",
                table: "inventory_balances");

            migrationBuilder.DropIndex(
                name: "ix_inventory_balances_product_variant_id",
                table: "inventory_balances");

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                table: "purchase_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                table: "inventory_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                table: "inventory_balances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            // Backfill legacy fashion rows before making the new warehouse key mandatory.
            // The migration is intentionally data-aware so existing Railway data remains usable.
            migrationBuilder.Sql("""
                INSERT INTO warehouses
                    (id, company_id, business_unit_id, code, name, is_active, is_default, created_at)
                SELECT gen_random_uuid(), c.id, bu.id, 'FASHION-MAIN', 'Kho Thời trang chính', true, true, NOW()
                FROM companies c
                LEFT JOIN business_units bu
                    ON bu.company_id = c.id AND bu.code = 'FASHION' AND bu.is_active = true AND bu.is_deleted = false
                WHERE NOT EXISTS
                    (SELECT 1 FROM warehouses w WHERE w.company_id = c.id AND w.code = 'FASHION-MAIN');
            """);

            // A few historical integration rows may outlive a deleted company record.
            // Keep those rows addressable as well; the warehouse table intentionally has
            // no hard Company FK so this remains a recoverable data migration.
            migrationBuilder.Sql("""
                INSERT INTO warehouses
                    (id, company_id, business_unit_id, code, name, is_active, is_default, created_at)
                SELECT gen_random_uuid(), legacy.company_id, NULL, 'FASHION-MAIN', 'Kho Thời trang chính', true, true, NOW()
                FROM (
                    SELECT company_id FROM purchase_receipts
                    UNION SELECT company_id FROM inventory_movements
                    UNION SELECT company_id FROM inventory_balances
                ) legacy
                WHERE NOT EXISTS
                    (SELECT 1 FROM warehouses w WHERE w.company_id = legacy.company_id AND w.code = 'FASHION-MAIN');
            """);

            migrationBuilder.Sql("""
                UPDATE purchase_receipts r
                SET warehouse_id = w.id
                FROM warehouses w
                WHERE r.warehouse_id IS NULL
                  AND w.company_id = r.company_id
                  AND w.code = 'FASHION-MAIN';

                UPDATE inventory_movements m
                SET warehouse_id = w.id
                FROM warehouses w
                WHERE m.warehouse_id IS NULL
                  AND w.company_id = m.company_id
                  AND w.code = 'FASHION-MAIN';

                UPDATE inventory_balances b
                SET warehouse_id = w.id
                FROM warehouses w
                WHERE b.warehouse_id IS NULL
                  AND w.company_id = b.company_id
                  AND w.code = 'FASHION-MAIN';
            """);

            migrationBuilder.AlterColumn<Guid>(
                name: "warehouse_id",
                table: "purchase_receipts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "warehouse_id",
                table: "inventory_movements",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "warehouse_id",
                table: "inventory_balances",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipts_warehouse_id",
                table: "purchase_receipts",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_company_id_warehouse_id_movement_date",
                table: "inventory_movements",
                columns: new[] { "company_id", "warehouse_id", "movement_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_warehouse_id",
                table: "inventory_movements",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_company_id_warehouse_id_product_variant_",
                table: "inventory_balances",
                columns: new[] { "company_id", "warehouse_id", "product_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_product_variant_id",
                table: "inventory_balances",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_warehouse_id",
                table: "inventory_balances",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_company_id_business_unit_id_is_default",
                table: "warehouses",
                columns: new[] { "company_id", "business_unit_id", "is_default" },
                unique: true,
                filter: "is_default = true AND is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_company_id_code",
                table: "warehouses",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_balances_warehouses_warehouse_id",
                table: "inventory_balances",
                column: "warehouse_id",
                principalTable: "warehouses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movements_warehouses_warehouse_id",
                table: "inventory_movements",
                column: "warehouse_id",
                principalTable: "warehouses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_receipts_warehouses_warehouse_id",
                table: "purchase_receipts",
                column: "warehouse_id",
                principalTable: "warehouses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_balances_warehouses_warehouse_id",
                table: "inventory_balances");

            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movements_warehouses_warehouse_id",
                table: "inventory_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_receipts_warehouses_warehouse_id",
                table: "purchase_receipts");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropIndex(
                name: "ix_purchase_receipts_warehouse_id",
                table: "purchase_receipts");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_company_id_warehouse_id_movement_date",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_warehouse_id",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "ix_inventory_balances_company_id_warehouse_id_product_variant_",
                table: "inventory_balances");

            migrationBuilder.DropIndex(
                name: "ix_inventory_balances_product_variant_id",
                table: "inventory_balances");

            migrationBuilder.DropIndex(
                name: "ix_inventory_balances_warehouse_id",
                table: "inventory_balances");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                table: "purchase_receipts");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                table: "inventory_balances");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_company_id_movement_date",
                table: "inventory_movements",
                columns: new[] { "company_id", "movement_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_company_id_product_variant_id",
                table: "inventory_balances",
                columns: new[] { "company_id", "product_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_product_variant_id",
                table: "inventory_balances",
                column: "product_variant_id",
                unique: true);
        }
    }
}
