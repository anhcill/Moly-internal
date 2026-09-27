using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Stores a per-enrollment tuition discount without changing the class list price.
/// This supports students who enroll in multiple courses/classes.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927120000_AddCscaStudentTuitionDiscount")]
public partial class AddCscaStudentTuitionDiscount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "discount_amount",
            table: "csca_class_students",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "discount_note",
            table: "csca_class_students",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "discount_amount", table: "csca_class_students");
        migrationBuilder.DropColumn(name: "discount_note", table: "csca_class_students");
    }
}
