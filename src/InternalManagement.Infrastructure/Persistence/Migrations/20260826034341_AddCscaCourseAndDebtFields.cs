using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaCourseAndDebtFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "course_id",
                table: "csca_classes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "debt_due_date",
                table: "csca_class_students",
                type: "timestamp with time zone",
                nullable: true);

            // Existing installations may contain classes created before the course relationship
            // existed. Give each tenant without a course a safe legacy parent, then backfill all
            // classes before making the relationship required.
            migrationBuilder.Sql("""
                INSERT INTO courses
                    (id, company_id, business_unit_id, course_source_id, title, slug, description,
                     thumbnail_url, price, status, version, created_at, is_deleted)
                SELECT gen_random_uuid(), c.id, NULL, 'LEGACY-CSCA-COURSE',
                       'Khóa học CSCA chưa phân loại', 'legacy-csca-course',
                       'Khóa học tạm dùng để tương thích dữ liệu lớp học cũ.',
                       NULL, 0, 'Draft', 1, NOW(), FALSE
                FROM companies c
                WHERE NOT EXISTS (SELECT 1 FROM courses existing WHERE existing.company_id = c.id);
            """);

            migrationBuilder.Sql("""
                UPDATE csca_classes cls
                SET course_id = (
                    SELECT course.id
                    FROM courses course
                    WHERE course.company_id = cls.company_id
                    ORDER BY course.created_at
                    LIMIT 1
                )
                WHERE cls.course_id IS NULL;
            """);

            migrationBuilder.AlterColumn<Guid>(
                name: "course_id",
                table: "csca_classes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_csca_classes_course_id",
                table: "csca_classes",
                column: "course_id");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_classes_courses_course_id",
                table: "csca_classes",
                column: "course_id",
                principalTable: "courses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_csca_classes_courses_course_id",
                table: "csca_classes");

            migrationBuilder.DropIndex(
                name: "ix_csca_classes_course_id",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "course_id",
                table: "csca_classes");

            migrationBuilder.DropColumn(
                name: "debt_due_date",
                table: "csca_class_students");
        }
    }
}
