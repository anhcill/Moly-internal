using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "csca_class_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    end_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    room = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    meeting_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csca_class_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_csca_class_schedules_csca_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "csca_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profit_allocations_reference_type_reference_id",
                table: "profit_allocations",
                columns: new[] { "reference_type", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_class_schedules_class_id_day_of_week_start_time_end_ti",
                table: "csca_class_schedules",
                columns: new[] { "class_id", "day_of_week", "start_time", "end_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "csca_class_schedules");

            migrationBuilder.DropIndex(
                name: "ix_profit_allocations_reference_type_reference_id",
                table: "profit_allocations");
        }
    }
}
