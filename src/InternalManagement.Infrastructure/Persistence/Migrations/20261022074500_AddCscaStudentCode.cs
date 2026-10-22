using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds student_code to csca_class_students for sequential CSCA student tracking.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022074500_AddCscaStudentCode")]
public partial class AddCscaStudentCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "student_code",
            table: "csca_class_students",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_csca_class_students_student_code",
            table: "csca_class_students",
            column: "student_code");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_csca_class_students_student_code",
            table: "csca_class_students");

        migrationBuilder.DropColumn(
            name: "student_code",
            table: "csca_class_students");
    }
}
