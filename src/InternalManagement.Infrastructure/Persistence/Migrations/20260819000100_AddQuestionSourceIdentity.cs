using System;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260819000100_AddQuestionSourceIdentity")]
    /// <inheritdoc />
    public partial class AddQuestionSourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_id",
                table: "questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                table: "questions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_questions_source_system_source_id",
                table: "questions",
                columns: new[] { "source_system", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_questions_source_system_source_id",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "source_system",
                table: "questions");
        }
    }
}
