using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persists the immutable LMS session reference used by attendance webhooks.
/// A Management lesson may have been scheduled locally first; the webhook then
/// attaches this key once and subsequent teacher edits update that same lesson.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921150000_AddCscaLmsAttendanceSessionLink")]
public partial class AddCscaLmsAttendanceSessionLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE csca_lesson_sessions
                ADD COLUMN IF NOT EXISTS external_session_id character varying(128);
            ALTER TABLE csca_lesson_sessions
                ADD COLUMN IF NOT EXISTS external_source character varying(100);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_csca_lesson_sessions_external_source_external_session_id
                ON csca_lesson_sessions (external_source, external_session_id)
                WHERE external_source IS NOT NULL AND external_session_id IS NOT NULL;
            """);
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
