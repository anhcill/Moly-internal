using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Binds each newly created sales order to the warehouse that reserves and
/// ships its stock. The column is nullable so historical orders remain valid.
/// </summary>
public partial class AddSalesOrderWarehouse : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "warehouse_id",
            table: "sales_orders",
            type: "uuid",
            nullable: true);

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

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_warehouse_id",
            table: "sales_orders",
            column: "warehouse_id");

        migrationBuilder.AddForeignKey(
            name: "fk_sales_orders_warehouses_warehouse_id",
            table: "sales_orders",
            column: "warehouse_id",
            principalTable: "warehouses",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
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
