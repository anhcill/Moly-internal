using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceWorkspaceCashAndReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "local_settlement_version",
                table: "csca_teacher_cost_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "planned_cash_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    transaction_type = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    recurrence = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    settled_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_cash_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    finance_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_receivables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    refunded_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_receivables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    refunded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    finance_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_refunds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_advance_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    teacher_advance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    returned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    finance_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_advance_returns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_advances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    returned_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    applied_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_advances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    teacher_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_cost_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_payment_allocations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    advance_applied_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    finance_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planned_cash_movements_company_id_idempotency_key",
                table: "planned_cash_movements",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_planned_cash_movements_company_id_planned_date_status",
                table: "planned_cash_movements",
                columns: new[] { "company_id", "planned_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_student_receipts_company_id_idempotency_key",
                table: "student_receipts",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_student_receivables_company_id_student_id_class_id",
                table: "student_receivables",
                columns: new[] { "company_id", "student_id", "class_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_company_id_idempotency_key",
                table: "student_refunds",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_advance_returns_company_id_idempotency_key",
                table: "teacher_advance_returns",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_advance_returns_company_id_teacher_advance_id_retur",
                table: "teacher_advance_returns",
                columns: new[] { "company_id", "teacher_advance_id", "returned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_advances_company_id_employee_id_issued_at",
                table: "teacher_advances",
                columns: new[] { "company_id", "employee_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_advances_company_id_idempotency_key",
                table: "teacher_advances",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_payment_allocations_company_id_teacher_payment_id_t",
                table: "teacher_payment_allocations",
                columns: new[] { "company_id", "teacher_payment_id", "teacher_cost_snapshot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_payment_allocations_teacher_cost_snapshot_id",
                table: "teacher_payment_allocations",
                column: "teacher_cost_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_payments_company_id_employee_id_paid_at",
                table: "teacher_payments",
                columns: new[] { "company_id", "employee_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_payments_company_id_idempotency_key",
                table: "teacher_payments",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planned_cash_movements");

            migrationBuilder.DropTable(
                name: "student_receipts");

            migrationBuilder.DropTable(
                name: "student_receivables");

            migrationBuilder.DropTable(
                name: "student_refunds");

            migrationBuilder.DropTable(
                name: "teacher_advance_returns");

            migrationBuilder.DropTable(
                name: "teacher_advances");

            migrationBuilder.DropTable(
                name: "teacher_payment_allocations");

            migrationBuilder.DropTable(
                name: "teacher_payments");

            migrationBuilder.DropColumn(
                name: "local_settlement_version",
                table: "csca_teacher_cost_snapshots");
        }
    }
}
