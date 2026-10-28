using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaTeacherCostSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "csca_teacher_cost_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_system = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_key = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    teacher_source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    teacher_lms_id = table.Column<long>(type: "bigint", nullable: false),
                    teacher_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    teacher_username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    course_source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    course_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    class_source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    class_lms_id = table.Column<long>(type: "bigint", nullable: false),
                    class_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    teaching_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    session_source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    session_lms_id = table.Column<long>(type: "bigint", nullable: false),
                    session_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    duration_hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    effective_hourly_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    accrued_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    projected_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    bonus_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    deduction_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source_calculated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csca_teacher_cost_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_csca_teacher_cost_snapshots_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_csca_teacher_cost_snapshots_company_id_class_source_id_star",
                table: "csca_teacher_cost_snapshots",
                columns: new[] { "company_id", "class_source_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_csca_teacher_cost_snapshots_company_id_is_current_start_time",
                table: "csca_teacher_cost_snapshots",
                columns: new[] { "company_id", "is_current", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_csca_teacher_cost_snapshots_company_id_source_system_source",
                table: "csca_teacher_cost_snapshots",
                columns: new[] { "company_id", "source_system", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_teacher_cost_snapshots_company_id_start_time",
                table: "csca_teacher_cost_snapshots",
                columns: new[] { "company_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_csca_teacher_cost_snapshots_employee_id",
                table: "csca_teacher_cost_snapshots",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "csca_teacher_cost_snapshots");
        }
    }
}
