using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds immutable LMS identities and version markers to the Management calendar
/// read model. LMS remains the only system allowed to edit this projection.
/// </summary>
public partial class AddCscaLmsCalendarProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "external_schedule_id",
            table: "csca_class_schedules",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "external_source",
            table: "csca_class_schedules",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "external_version",
            table: "csca_class_schedules",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "status",
            table: "csca_class_schedules",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "Active");

        migrationBuilder.AddColumn<DateOnly>(
            name: "start_date",
            table: "csca_class_schedules",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "end_date",
            table: "csca_class_schedules",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "timezone",
            table: "csca_class_schedules",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "Asia/Ho_Chi_Minh");

        migrationBuilder.AddColumn<string>(
            name: "title",
            table: "csca_class_schedules",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "external_version",
            table: "csca_lesson_sessions",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "ix_csca_class_schedules_external_source_external_schedule_id",
            table: "csca_class_schedules",
            columns: new[] { "external_source", "external_schedule_id" },
            unique: true,
            filter: "external_source IS NOT NULL AND external_schedule_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_csca_class_schedules_external_source_external_schedule_id",
            table: "csca_class_schedules");

        migrationBuilder.DropColumn(name: "external_schedule_id", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "external_source", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "external_version", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "status", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "start_date", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "end_date", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "timezone", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "title", table: "csca_class_schedules");
        migrationBuilder.DropColumn(name: "external_version", table: "csca_lesson_sessions");
    }
}
