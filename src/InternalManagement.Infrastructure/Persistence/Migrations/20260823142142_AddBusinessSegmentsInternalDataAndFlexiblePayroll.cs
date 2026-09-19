using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessSegmentsInternalDataAndFlexiblePayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "standard_work_days",
                table: "payslips",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "actual_work_days",
                table: "payslips",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<decimal>(
                name: "actual_shifts",
                table: "payslips",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "actual_work_hours",
                table: "payslips",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                table: "payslips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FULL_TIME");

            migrationBuilder.AddColumn<decimal>(
                name: "health_insurance",
                table: "payslips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "kpi_bonus",
                table: "payslips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "part_time_calculation_method",
                table: "payslips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "part_time_unit_rate",
                table: "payslips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_deductions",
                table: "payslips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_income",
                table: "payslips",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "cv_url_or_path",
                table: "employees",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FULL_TIME");

            migrationBuilder.AddColumn<string>(
                name: "experience",
                table: "employees",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "part_time_calculation_method",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "part_time_unit_rate",
                table: "employees",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_summary",
                table: "employees",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "skills",
                table: "employees",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "internal_customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_segment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    contact_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_internal_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_internal_customers_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalTable: "business_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_internal_customers_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "internal_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_segment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    storage_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tags_csv = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    metadata_json = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_internal_resources", x => x.id);
                    table.CheckConstraint("ck_internal_resources_segment_type", "(business_segment = 'TECHNOLOGY_EDUCATION' AND resource_type IN ('EXAM', 'DOCUMENT')) OR (business_segment = 'FASHION' AND resource_type IN ('PLAN', 'DESIGN_SAMPLE', 'DOCUMENT'))");
                    table.ForeignKey(
                        name: "fk_internal_resources_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalTable: "business_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_internal_resources_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_internal_customers_business_unit_id",
                table: "internal_customers",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_internal_customers_company_id_business_unit_id_business_seg",
                table: "internal_customers",
                columns: new[] { "company_id", "business_unit_id", "business_segment", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_internal_customers_company_id_business_unit_id_business_seg1",
                table: "internal_customers",
                columns: new[] { "company_id", "business_unit_id", "business_segment", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_internal_customers_company_id_business_unit_id_business_seg2",
                table: "internal_customers",
                columns: new[] { "company_id", "business_unit_id", "business_segment", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_internal_resources_business_unit_id",
                table: "internal_resources",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_internal_resources_company_id_business_unit_id_business_seg",
                table: "internal_resources",
                columns: new[] { "company_id", "business_unit_id", "business_segment", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_internal_resources_company_id_business_unit_id_business_seg1",
                table: "internal_resources",
                columns: new[] { "company_id", "business_unit_id", "business_segment", "title" });

            migrationBuilder.CreateIndex(
                name: "ix_internal_resources_company_id_business_unit_id_business_seg2",
                table: "internal_resources",
                columns: new[] { "company_id", "business_unit_id", "business_segment", "resource_type", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "internal_customers");

            migrationBuilder.DropTable(
                name: "internal_resources");

            migrationBuilder.DropColumn(
                name: "actual_shifts",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "actual_work_hours",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "employment_type",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "health_insurance",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "kpi_bonus",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "part_time_calculation_method",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "part_time_unit_rate",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "total_deductions",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "total_income",
                table: "payslips");

            migrationBuilder.DropColumn(
                name: "cv_url_or_path",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "experience",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "part_time_calculation_method",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "part_time_unit_rate",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "professional_summary",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "skills",
                table: "employees");

            migrationBuilder.AlterColumn<decimal>(
                name: "standard_work_days",
                table: "payslips",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "actual_work_days",
                table: "payslips",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2);
        }
    }
}
