using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingCostingAndProfitSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "actual_cogs",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "affiliate_fee",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "cost_status",
                table: "sales_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_amount",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "net_revenue",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "payment_fee",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "platform_fee",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "profit",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "shipping_customer_paid",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "shipping_shop_subsidy",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "sales_orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "cost_status",
                table: "sales_order_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost_snapshot",
                table: "sales_order_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "cost_status",
                table: "product_variants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_actual_cost_at",
                table: "product_variants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sourcing_type",
                table: "product_variants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "boms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boms", x => x.id);
                    table.ForeignKey(
                        name: "fk_boms_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_fee_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    platform_fee_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    affiliate_fee_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    payment_fee_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    fixed_payment_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    default_shipping_subsidy = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_fee_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_materials", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_cost_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    channel_fee_policy_version = table.Column<int>(type: "integer", nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    net_sales_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    platform_fee = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    affiliate_fee = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    payment_fee = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    shipping_subsidy = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actual_cogs = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    profit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    margin = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cost_status = table.Column<string>(type: "text", nullable: false),
                    snapshotted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_cost_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_cost_snapshots_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    cost_status = table.Column<string>(type: "text", nullable: false),
                    planned_quantity = table.Column<int>(type: "integer", nullable: false),
                    good_quantity = table.Column<int>(type: "integer", nullable: false),
                    defective_quantity = table.Column<int>(type: "integer", nullable: false),
                    rework_quantity = table.Column<int>(type: "integer", nullable: false),
                    standard_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_material_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_labor_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_outside_processing_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_overhead_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_scrap_rework_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_orders_boms_bom_id",
                        column: x => x.bom_id,
                        principalTable: "boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_production_orders_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bom_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    waste_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bom_items_boms_bom_id",
                        column: x => x.bom_id,
                        principalTable: "boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bom_items_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_remaining = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_lots", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_lots_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_material_lots_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "order_cost_snapshot_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_cost_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_selling_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_status = table.Column<string>(type: "text", nullable: false),
                    total_cogs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_cost_snapshot_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_cost_snapshot_items_order_cost_snapshots_order_cost_s",
                        column: x => x.order_cost_snapshot_id,
                        principalTable: "order_cost_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_cost_snapshot_items_product_variants_product_variant_",
                        column: x => x.product_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_outside_processing = table.Column<bool>(type: "boolean", nullable: false),
                    rate_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actual_units = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_operations_production_orders_production_order_id",
                        column: x => x.production_order_id,
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_order_outputs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_quantity = table.Column<int>(type: "integer", nullable: false),
                    good_quantity = table.Column<int>(type: "integer", nullable: false),
                    defective_quantity = table.Column<int>(type: "integer", nullable: false),
                    rework_quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_order_outputs", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_order_outputs_product_variants_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_order_outputs_production_orders_production_order",
                        column: x => x.production_order_id,
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "material_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_type = table.Column<string>(type: "text", nullable: false),
                    quantity_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference_type = table.Column<string>(type: "text", nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_material_movements_material_lots_material_lot_id",
                        column: x => x.material_lot_id,
                        principalTable: "material_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_material_movements_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_order_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    production_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    size = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    planned_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_order_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_order_materials_material_lots_material_lot_id",
                        column: x => x.material_lot_id,
                        principalTable: "material_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_production_order_materials_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_order_materials_production_orders_production_ord",
                        column: x => x.production_order_id,
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bom_items_bom_id",
                table: "bom_items",
                column: "bom_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_items_material_id",
                table: "bom_items",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_boms_company_id_product_id_version_number",
                table: "boms",
                columns: new[] { "company_id", "product_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boms_product_id",
                table: "boms",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_fee_policies_company_id_channel_version_number",
                table: "channel_fee_policies",
                columns: new[] { "company_id", "channel", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_material_lots_company_id_lot_number",
                table: "material_lots",
                columns: new[] { "company_id", "lot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_material_lots_material_id",
                table: "material_lots",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_lots_supplier_id",
                table: "material_lots",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_movements_company_id_material_id_movement_date",
                table: "material_movements",
                columns: new[] { "company_id", "material_id", "movement_date" });

            migrationBuilder.CreateIndex(
                name: "ix_material_movements_material_id",
                table: "material_movements",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_movements_material_lot_id",
                table: "material_movements",
                column: "material_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_materials_company_id_code",
                table: "materials",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_cost_snapshot_items_order_cost_snapshot_id",
                table: "order_cost_snapshot_items",
                column: "order_cost_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_cost_snapshot_items_product_variant_id",
                table: "order_cost_snapshot_items",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_cost_snapshots_company_id_sales_order_id_snapshotted_",
                table: "order_cost_snapshots",
                columns: new[] { "company_id", "sales_order_id", "snapshotted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_cost_snapshots_sales_order_id",
                table: "order_cost_snapshots",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_operations_production_order_id_sequence",
                table: "production_operations",
                columns: new[] { "production_order_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_order_materials_material_id",
                table: "production_order_materials",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_materials_material_lot_id",
                table: "production_order_materials",
                column: "material_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_materials_production_order_id",
                table: "production_order_materials",
                column: "production_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_outputs_product_variant_id",
                table: "production_order_outputs",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_outputs_production_order_id_product_varian",
                table: "production_order_outputs",
                columns: new[] { "production_order_id", "product_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_bom_id",
                table: "production_orders",
                column: "bom_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_company_id_order_number",
                table: "production_orders",
                columns: new[] { "company_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_product_id",
                table: "production_orders",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bom_items");

            migrationBuilder.DropTable(
                name: "channel_fee_policies");

            migrationBuilder.DropTable(
                name: "material_movements");

            migrationBuilder.DropTable(
                name: "order_cost_snapshot_items");

            migrationBuilder.DropTable(
                name: "production_operations");

            migrationBuilder.DropTable(
                name: "production_order_materials");

            migrationBuilder.DropTable(
                name: "production_order_outputs");

            migrationBuilder.DropTable(
                name: "order_cost_snapshots");

            migrationBuilder.DropTable(
                name: "material_lots");

            migrationBuilder.DropTable(
                name: "production_orders");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "boms");

            migrationBuilder.DropColumn(
                name: "actual_cogs",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "affiliate_fee",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "cost_status",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "gross_amount",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "net_revenue",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "payment_fee",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "platform_fee",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "profit",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "shipping_customer_paid",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "shipping_shop_subsidy",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "cost_status",
                table: "sales_order_items");

            migrationBuilder.DropColumn(
                name: "unit_cost_snapshot",
                table: "sales_order_items");

            migrationBuilder.DropColumn(
                name: "cost_status",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "last_actual_cost_at",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "sourcing_type",
                table: "product_variants");
        }
    }
}
