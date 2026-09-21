using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persists the immutable LMS session reference used by attendance webhooks.
/// A Management lesson may have been scheduled locally first; the webhook then
/// attaches this key once and subsequent teacher edits update that same lesson.
/// </summary>
public partial class AddCscaLmsAttendanceSessionLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "external_session_id",
            table: "csca_lesson_sessions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "external_source",
            table: "csca_lesson_sessions",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_csca_lesson_sessions_external_source_external_session_id",
            table: "csca_lesson_sessions",
            columns: new[] { "external_source", "external_session_id" },
            unique: true,
            filter: "external_source IS NOT NULL AND external_session_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_csca_lesson_sessions_external_source_external_session_id",
            table: "csca_lesson_sessions");

        migrationBuilder.DropColumn(name: "external_session_id", table: "csca_lesson_sessions");
        migrationBuilder.DropColumn(name: "external_source", table: "csca_lesson_sessions");
    }
}
