using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaRecordingClassAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "class_type",
                table: "csca_classes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "LIVE");

            migrationBuilder.AddColumn<Guid>(
                name: "source_class_id",
                table: "csca_classes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_classes_source_class_id",
                table: "csca_classes",
                column: "source_class_id");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_classes_csca_classes_source_class_id",
                table: "csca_classes",
                column: "source_class_id",
                principalTable: "csca_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_csca_classes_csca_classes_source_class_id",
                table: "csca_classes");

            migrationBuilder.DropIndex(
                name: "ix_csca_classes_source_class_id",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "class_type",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "source_class_id",
                table: "csca_classes");
        }
    }
}
