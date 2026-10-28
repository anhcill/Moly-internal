using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceWorkspaceSettlementSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_teacher_payments_employee_id",
                table: "teacher_payments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_payment_allocations_teacher_payment_id",
                table: "teacher_payment_allocations",
                column: "teacher_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_advances_employee_id",
                table: "teacher_advances",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_advance_returns_teacher_advance_id",
                table: "teacher_advance_returns",
                column: "teacher_advance_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_student_receivable_id",
                table: "student_refunds",
                column: "student_receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_receivables_class_id",
                table: "student_receivables",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_receivables_student_id",
                table: "student_receivables",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_receipts_student_receivable_id",
                table: "student_receipts",
                column: "student_receivable_id");

            migrationBuilder.AddForeignKey(
                name: "fk_student_receipts_student_receivables_student_receivable_id",
                table: "student_receipts",
                column: "student_receivable_id",
                principalTable: "student_receivables",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_student_receivables_csca_class_students_student_id",
                table: "student_receivables",
                column: "student_id",
                principalTable: "csca_class_students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_student_receivables_csca_classes_class_id",
                table: "student_receivables",
                column: "class_id",
                principalTable: "csca_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_student_refunds_student_receivables_student_receivable_id",
                table: "student_refunds",
                column: "student_receivable_id",
                principalTable: "student_receivables",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_teacher_advance_returns_teacher_advances_teacher_advance_id",
                table: "teacher_advance_returns",
                column: "teacher_advance_id",
                principalTable: "teacher_advances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_teacher_advances_employees_employee_id",
                table: "teacher_advances",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_teacher_payment_allocations_csca_teacher_cost_snapshots_tea",
                table: "teacher_payment_allocations",
                column: "teacher_cost_snapshot_id",
                principalTable: "csca_teacher_cost_snapshots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_teacher_payment_allocations_teacher_payments_teacher_paymen",
                table: "teacher_payment_allocations",
                column: "teacher_payment_id",
                principalTable: "teacher_payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_teacher_payments_employees_employee_id",
                table: "teacher_payments",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_student_receipts_student_receivables_student_receivable_id",
                table: "student_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_student_receivables_csca_class_students_student_id",
                table: "student_receivables");

            migrationBuilder.DropForeignKey(
                name: "fk_student_receivables_csca_classes_class_id",
                table: "student_receivables");

            migrationBuilder.DropForeignKey(
                name: "fk_student_refunds_student_receivables_student_receivable_id",
                table: "student_refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_teacher_advance_returns_teacher_advances_teacher_advance_id",
                table: "teacher_advance_returns");

            migrationBuilder.DropForeignKey(
                name: "fk_teacher_advances_employees_employee_id",
                table: "teacher_advances");

            migrationBuilder.DropForeignKey(
                name: "fk_teacher_payment_allocations_csca_teacher_cost_snapshots_tea",
                table: "teacher_payment_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_teacher_payment_allocations_teacher_payments_teacher_paymen",
                table: "teacher_payment_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_teacher_payments_employees_employee_id",
                table: "teacher_payments");

            migrationBuilder.DropIndex(
                name: "ix_teacher_payments_employee_id",
                table: "teacher_payments");

            migrationBuilder.DropIndex(
                name: "ix_teacher_payment_allocations_teacher_payment_id",
                table: "teacher_payment_allocations");

            migrationBuilder.DropIndex(
                name: "ix_teacher_advances_employee_id",
                table: "teacher_advances");

            migrationBuilder.DropIndex(
                name: "ix_teacher_advance_returns_teacher_advance_id",
                table: "teacher_advance_returns");

            migrationBuilder.DropIndex(
                name: "ix_student_refunds_student_receivable_id",
                table: "student_refunds");

            migrationBuilder.DropIndex(
                name: "ix_student_receivables_class_id",
                table: "student_receivables");

            migrationBuilder.DropIndex(
                name: "ix_student_receivables_student_id",
                table: "student_receivables");

            migrationBuilder.DropIndex(
                name: "ix_student_receipts_student_receivable_id",
                table: "student_receipts");
        }
    }
}
