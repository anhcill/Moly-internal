using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261121123000_AddRecordingCurriculumReference")]
    public partial class AddRecordingCurriculumReference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "curriculum_excluded_session_ids_json",
                table: "csca_classes",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTime>(
                name: "curriculum_reference_updated_at",
                table: "csca_classes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "curriculum_source_class_id",
                table: "csca_classes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_classes_curriculum_source_class_id",
                table: "csca_classes",
                column: "curriculum_source_class_id");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_classes_csca_classes_curriculum_source_class_id",
                table: "csca_classes",
                column: "curriculum_source_class_id",
                principalTable: "csca_classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE csca_classes
                ADD CONSTRAINT ck_csca_classes_curriculum_reference
                CHECK (
                    curriculum_source_class_id IS NULL
                    OR (
                        class_type = 'RECORDING'
                        AND recording_content_mode = 'INDEPENDENT'
                        AND source_class_id IS NULL
                    )
                );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE csca_classes
                DROP CONSTRAINT IF EXISTS ck_csca_classes_curriculum_reference;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_csca_classes_csca_classes_curriculum_source_class_id",
                table: "csca_classes");

            migrationBuilder.DropIndex(
                name: "ix_csca_classes_curriculum_source_class_id",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "curriculum_excluded_session_ids_json",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "curriculum_reference_updated_at",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "curriculum_source_class_id",
                table: "csca_classes");
        }
    }
}
