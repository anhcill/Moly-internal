using System;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Binds each newly created sales order to the warehouse that reserves and
/// ships its stock. The column is nullable so historical orders remain valid.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901100000_AddSalesOrderWarehouse")]
public partial class AddSalesOrderWarehouse : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Some environments already have this column, but not its migration marker.
        migrationBuilder.Sql("ALTER TABLE sales_orders ADD COLUMN IF NOT EXISTS warehouse_id uuid;");

        // Freeze historical orders to the warehouse that was default at upgrade time.
        // New orders always select and persist their source warehouse explicitly.
        migrationBuilder.Sql("""
            UPDATE sales_orders AS so
            SET warehouse_id = (
                SELECT w.id
                FROM warehouses AS w
                WHERE w.company_id = so.company_id
                  AND w.business_unit_id IS NOT DISTINCT FROM so.business_unit_id
                ORDER BY w.is_default DESC, w.code
                LIMIT 1
            )
            WHERE so.warehouse_id IS NULL;
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS ix_sales_orders_warehouse_id ON sales_orders (warehouse_id);
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_sales_orders_warehouses_warehouse_id'
                      AND conrelid = 'sales_orders'::regclass
                ) THEN
                    ALTER TABLE sales_orders
                    ADD CONSTRAINT fk_sales_orders_warehouses_warehouse_id
                    FOREIGN KEY (warehouse_id) REFERENCES warehouses (id) ON DELETE RESTRICT;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_sales_orders_warehouses_warehouse_id",
            table: "sales_orders");

        migrationBuilder.DropIndex(
            name: "ix_sales_orders_warehouse_id",
            table: "sales_orders");

        migrationBuilder.DropColumn(
            name: "warehouse_id",
            table: "sales_orders");
    }
}
