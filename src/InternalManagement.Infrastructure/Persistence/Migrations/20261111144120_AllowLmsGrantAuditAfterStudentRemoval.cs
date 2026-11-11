using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowLmsGrantAuditAfterStudentRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lms_access_grants_csca_class_students_csca_class_student_id",
                table: "lms_access_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lms_account_links_identity",
                table: "lms_account_links");

            migrationBuilder.AlterColumn<Guid>(
                name: "csca_class_student_id",
                table: "lms_access_grants",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lms_account_links_identity",
                table: "lms_account_links",
                sql: "party_id IS NOT NULL OR csca_class_student_id IS NOT NULL OR length(trim(external_student_id)) > 0");

            migrationBuilder.AddForeignKey(
                name: "fk_lms_access_grants_csca_class_students_csca_class_student_id",
                table: "lms_access_grants",
                column: "csca_class_student_id",
                principalTable: "csca_class_students",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lms_access_grants_csca_class_students_csca_class_student_id",
                table: "lms_access_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lms_account_links_identity",
                table: "lms_account_links");

            migrationBuilder.AlterColumn<Guid>(
                name: "csca_class_student_id",
                table: "lms_access_grants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_lms_account_links_identity",
                table: "lms_account_links",
                sql: "party_id IS NOT NULL OR csca_class_student_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_lms_access_grants_csca_class_students_csca_class_student_id",
                table: "lms_access_grants",
                column: "csca_class_student_id",
                principalTable: "csca_class_students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
