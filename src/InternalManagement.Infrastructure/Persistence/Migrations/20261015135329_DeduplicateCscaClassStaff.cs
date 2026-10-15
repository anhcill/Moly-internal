using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeduplicateCscaClassStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY class_id, employee_id
                               ORDER BY updated_at DESC NULLS LAST, created_at DESC, id DESC
                           ) AS duplicate_rank
                    FROM csca_class_staffs
                )
                DELETE FROM csca_class_staffs AS staff
                USING ranked
                WHERE staff.id = ranked.id
                  AND ranked.duplicate_rank > 1;
                """);

            migrationBuilder.DropIndex(
                name: "ix_csca_class_staffs_class_id",
                table: "csca_class_staffs");

            migrationBuilder.CreateIndex(
                name: "ix_csca_class_staffs_class_id_employee_id",
                table: "csca_class_staffs",
                columns: new[] { "class_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_csca_class_staffs_class_id_employee_id",
                table: "csca_class_staffs");

            migrationBuilder.CreateIndex(
                name: "ix_csca_class_staffs_class_id",
                table: "csca_class_staffs",
                column: "class_id");
        }
    }
}
