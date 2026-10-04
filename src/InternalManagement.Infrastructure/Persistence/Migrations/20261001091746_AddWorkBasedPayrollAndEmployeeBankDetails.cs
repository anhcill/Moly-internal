using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddWorkBasedPayrollAndEmployeeBankDetails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "work_earnings",
            table: "payslips",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            table: "payslips",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            table: "payroll_periods",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.AddColumn<string>(
            name: "bank_account_holder",
            table: "employees",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "bank_account_number",
            table: "employees",
            type: "character varying(34)",
            maxLength: 34,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "bank_name",
            table: "employees",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "payroll_work_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                payroll_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                work_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                reference_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                evidence_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                work_date = table.Column<DateOnly>(type: "date", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                unit_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                is_voided = table.Column<bool>(type: "boolean", nullable: false),
                voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                voided_by = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payroll_work_entries", x => x.id);
                table.ForeignKey(
                    name: "fk_payroll_work_entries_employees_employee_id",
                    column: x => x.employee_id,
                    principalTable: "employees",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_payroll_work_entries_payroll_periods_payroll_period_id",
                    column: x => x.payroll_period_id,
                    principalTable: "payroll_periods",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_payroll_work_entries_company_id_employee_id_work_type_refer",
            table: "payroll_work_entries",
            columns: new[] { "company_id", "employee_id", "work_type", "reference_code" },
            unique: true,
            filter: "NOT is_voided");

        migrationBuilder.CreateIndex(
            name: "ix_payroll_work_entries_employee_id",
            table: "payroll_work_entries",
            column: "employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_payroll_work_entries_payroll_period_id_employee_id",
            table: "payroll_work_entries",
            columns: new[] { "payroll_period_id", "employee_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "payroll_work_entries");
        migrationBuilder.DropColumn(name: "work_earnings", table: "payslips");
        migrationBuilder.DropColumn(name: "xmin", table: "payslips");
        migrationBuilder.DropColumn(name: "xmin", table: "payroll_periods");
        migrationBuilder.DropColumn(name: "bank_account_holder", table: "employees");
        migrationBuilder.DropColumn(name: "bank_account_number", table: "employees");
        migrationBuilder.DropColumn(name: "bank_name", table: "employees");
    }
}
