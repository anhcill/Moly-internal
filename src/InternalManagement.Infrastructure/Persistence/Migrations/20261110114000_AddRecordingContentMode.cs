using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordingContentMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recording_content_mode",
                table: "csca_classes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE csca_classes
                SET recording_content_mode = 'MIRROR'
                WHERE class_type = 'RECORDING' AND recording_content_mode IS NULL;

                ALTER TABLE csca_classes
                ADD CONSTRAINT ck_csca_classes_recording_content_mode
                CHECK (
                    (class_type = 'LIVE' AND recording_content_mode IS NULL AND source_class_id IS NULL)
                    OR
                    (class_type = 'RECORDING' AND recording_content_mode = 'MIRROR' AND source_class_id IS NOT NULL)
                    OR
                    (class_type = 'RECORDING' AND recording_content_mode = 'INDEPENDENT' AND source_class_id IS NULL)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE csca_classes
                DROP CONSTRAINT IF EXISTS ck_csca_classes_recording_content_mode;
                """);

            migrationBuilder.DropColumn(
                name: "recording_content_mode",
                table: "csca_classes");
        }
    }
}
