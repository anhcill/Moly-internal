using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailedSellingCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "advertising_cost",
                table: "sales_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "other_selling_expense",
                table: "sales_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "packaging_cost",
                table: "sales_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "advertising_cost",
                table: "order_cost_snapshots",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "other_selling_expense",
                table: "order_cost_snapshots",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "packaging_cost",
                table: "order_cost_snapshots",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "advertising_cost",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "other_selling_expense",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "packaging_cost",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "advertising_cost",
                table: "order_cost_snapshots");

            migrationBuilder.DropColumn(
                name: "other_selling_expense",
                table: "order_cost_snapshots");

            migrationBuilder.DropColumn(
                name: "packaging_cost",
                table: "order_cost_snapshots");
        }
    }
}
