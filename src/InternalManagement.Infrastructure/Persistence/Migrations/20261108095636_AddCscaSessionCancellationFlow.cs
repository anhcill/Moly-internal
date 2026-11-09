using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaSessionCancellationFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "csca_lesson_sessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "csca_lesson_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancelled_by",
                table: "csca_lesson_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_payable",
                table: "csca_lesson_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replacement_for_session_id",
                table: "csca_lesson_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_kind",
                table: "csca_lesson_sessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.CreateIndex(
                name: "ix_csca_lesson_sessions_replacement_for_session_id",
                table: "csca_lesson_sessions",
                column: "replacement_for_session_id",
                unique: true,
                filter: "replacement_for_session_id IS NOT NULL AND status <> 'Cancelled'");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_lesson_sessions_csca_lesson_sessions_replacement_for_s",
                table: "csca_lesson_sessions",
                column: "replacement_for_session_id",
                principalTable: "csca_lesson_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_csca_lesson_sessions_csca_lesson_sessions_replacement_for_s",
                table: "csca_lesson_sessions");

            migrationBuilder.DropIndex(
                name: "ix_csca_lesson_sessions_replacement_for_session_id",
                table: "csca_lesson_sessions");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "csca_lesson_sessions");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "csca_lesson_sessions");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                table: "csca_lesson_sessions");

            migrationBuilder.DropColumn(
                name: "is_payable",
                table: "csca_lesson_sessions");

            migrationBuilder.DropColumn(
                name: "replacement_for_session_id",
                table: "csca_lesson_sessions");

            migrationBuilder.DropColumn(
                name: "session_kind",
                table: "csca_lesson_sessions");
        }
    }
}
