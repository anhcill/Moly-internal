using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaClassroomSessionsAndAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "classroom_id",
                table: "csca_class_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "csca_classrooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csca_classrooms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "csca_lesson_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    classroom_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lesson_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    end_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    meeting_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csca_lesson_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_csca_lesson_sessions_csca_class_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "csca_class_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_csca_lesson_sessions_csca_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "csca_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_csca_lesson_sessions_csca_classrooms_classroom_id",
                        column: x => x.classroom_id,
                        principalTable: "csca_classrooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "csca_lesson_attendances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    check_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csca_lesson_attendances", x => x.id);
                    table.ForeignKey(
                        name: "fk_csca_lesson_attendances_csca_class_students_student_id",
                        column: x => x.student_id,
                        principalTable: "csca_class_students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_csca_lesson_attendances_csca_lesson_sessions_lesson_session",
                        column: x => x.lesson_session_id,
                        principalTable: "csca_lesson_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_csca_class_schedules_classroom_id",
                table: "csca_class_schedules",
                column: "classroom_id");

            migrationBuilder.CreateIndex(
                name: "ix_csca_classrooms_company_id_code",
                table: "csca_classrooms",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_lesson_attendances_lesson_session_id_student_id",
                table: "csca_lesson_attendances",
                columns: new[] { "lesson_session_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_lesson_attendances_student_id",
                table: "csca_lesson_attendances",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_csca_lesson_sessions_class_id_lesson_date_start_time_end_ti",
                table: "csca_lesson_sessions",
                columns: new[] { "class_id", "lesson_date", "start_time", "end_time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_lesson_sessions_classroom_id_lesson_date_start_time_en",
                table: "csca_lesson_sessions",
                columns: new[] { "classroom_id", "lesson_date", "start_time", "end_time" });

            migrationBuilder.CreateIndex(
                name: "ix_csca_lesson_sessions_schedule_id",
                table: "csca_lesson_sessions",
                column: "schedule_id");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_class_schedules_csca_classrooms_classroom_id",
                table: "csca_class_schedules",
                column: "classroom_id",
                principalTable: "csca_classrooms",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_csca_class_schedules_csca_classrooms_classroom_id",
                table: "csca_class_schedules");

            migrationBuilder.DropTable(
                name: "csca_lesson_attendances");

            migrationBuilder.DropTable(
                name: "csca_lesson_sessions");

            migrationBuilder.DropTable(
                name: "csca_classrooms");

            migrationBuilder.DropIndex(
                name: "ix_csca_class_schedules_classroom_id",
                table: "csca_class_schedules");

            migrationBuilder.DropColumn(
                name: "classroom_id",
                table: "csca_class_schedules");
        }
    }
}
