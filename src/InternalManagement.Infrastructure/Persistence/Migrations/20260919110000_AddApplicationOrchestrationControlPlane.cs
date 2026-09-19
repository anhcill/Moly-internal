using System;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Introduces the tenant-scoped application registry, canonical membership,
/// scoped roles, mappings and central entitlement ledger. It intentionally
/// leaves the concrete CSCA LMS bridge intact so existing LMS grants continue
/// to work while other child applications are onboarded.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260919110000_AddApplicationOrchestrationControlPlane")]
public partial class AddApplicationOrchestrationControlPlane : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "managed_applications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<string>(type: "text", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_managed_applications", x => x.id));

        migrationBuilder.CreateTable(
            name: "application_course_maps",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                managed_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                external_course_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                external_course_numeric_id = table.Column<long>(type: "bigint", nullable: true),
                external_course_slug = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_application_course_maps", x => x.id);
                table.ForeignKey("fk_application_course_maps_courses_course_id", x => x.course_id, "courses", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_application_course_maps_managed_applications_managed_application_id", x => x.managed_application_id, "managed_applications", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "application_memberships",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                managed_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                party_id = table.Column<Guid>(type: "uuid", nullable: false),
                external_user_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_application_memberships", x => x.id);
                table.ForeignKey("fk_application_memberships_managed_applications_managed_application_id", x => x.managed_application_id, "managed_applications", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_application_memberships_parties_party_id", x => x.party_id, "parties", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "application_class_maps",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                managed_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                csca_class_id = table.Column<Guid>(type: "uuid", nullable: false),
                application_course_map_id = table.Column<Guid>(type: "uuid", nullable: true),
                external_class_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_application_class_maps", x => x.id);
                table.ForeignKey("fk_application_class_maps_application_course_maps_application_course_map_id", x => x.application_course_map_id, "application_course_maps", "id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("fk_application_class_maps_csca_classes_csca_class_id", x => x.csca_class_id, "csca_classes", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_application_class_maps_managed_applications_managed_application_id", x => x.managed_application_id, "managed_applications", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "application_role_assignments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                application_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_application_role_assignments", x => x.id);
                table.CheckConstraint("ck_application_role_assignments_scope", "(scope_type = 'Application' AND scope_id IS NULL) OR (scope_type <> 'Application' AND scope_id IS NOT NULL)");
                table.ForeignKey("fk_application_role_assignments_application_memberships_application_membership_id", x => x.application_membership_id, "application_memberships", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "application_entitlements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                application_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                application_course_map_id = table.Column<Guid>(type: "uuid", nullable: false),
                application_class_map_id = table.Column<Guid>(type: "uuid", nullable: true),
                csca_class_student_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_payment_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_application_entitlements", x => x.id);
                table.CheckConstraint("ck_application_entitlements_valid_range", "valid_until IS NULL OR valid_until > valid_from");
                table.ForeignKey("fk_application_entitlements_application_class_maps_application_class_map_id", x => x.application_class_map_id, "application_class_maps", "id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("fk_application_entitlements_application_course_maps_application_course_map_id", x => x.application_course_map_id, "application_course_maps", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_application_entitlements_application_memberships_application_membership_id", x => x.application_membership_id, "application_memberships", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_application_entitlements_csca_class_students_csca_class_student_id", x => x.csca_class_student_id, "csca_class_students", "id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("ix_managed_apps_company_code", "managed_applications", new[] { "company_id", "code" }, unique: true);
        migrationBuilder.CreateIndex("ix_managed_apps_company_status", "managed_applications", new[] { "company_id", "status" });
        migrationBuilder.CreateIndex("ix_app_course_maps_course", "application_course_maps", "course_id");
        migrationBuilder.CreateIndex("ix_app_course_maps_application", "application_course_maps", "managed_application_id");
        migrationBuilder.CreateIndex("ix_app_course_maps_company_app_course", "application_course_maps", new[] { "company_id", "managed_application_id", "course_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_course_maps_company_app_ext_id", "application_course_maps", new[] { "company_id", "managed_application_id", "external_course_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_course_maps_company_app_ext_num", "application_course_maps", new[] { "company_id", "managed_application_id", "external_course_numeric_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_course_maps_company_app_status", "application_course_maps", new[] { "company_id", "managed_application_id", "status" });
        migrationBuilder.CreateIndex("ix_app_memberships_application", "application_memberships", "managed_application_id");
        migrationBuilder.CreateIndex("ix_app_memberships_party", "application_memberships", "party_id");
        migrationBuilder.CreateIndex("ix_app_memberships_company_app_party", "application_memberships", new[] { "company_id", "managed_application_id", "party_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_memberships_company_app_user", "application_memberships", new[] { "company_id", "managed_application_id", "external_user_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_memberships_company_app_status", "application_memberships", new[] { "company_id", "managed_application_id", "status" });
        migrationBuilder.CreateIndex("ix_app_class_maps_course_map", "application_class_maps", "application_course_map_id");
        migrationBuilder.CreateIndex("ix_app_class_maps_csca_class", "application_class_maps", "csca_class_id");
        migrationBuilder.CreateIndex("ix_app_class_maps_application", "application_class_maps", "managed_application_id");
        migrationBuilder.CreateIndex("ix_app_class_maps_company_app_class", "application_class_maps", new[] { "company_id", "managed_application_id", "csca_class_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_class_maps_company_app_ext_id", "application_class_maps", new[] { "company_id", "managed_application_id", "external_class_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_class_maps_company_app_status", "application_class_maps", new[] { "company_id", "managed_application_id", "status" });
        migrationBuilder.CreateIndex("ix_app_roles_membership", "application_role_assignments", "application_membership_id");
        migrationBuilder.CreateIndex("ix_app_roles_membership_role_scope", "application_role_assignments", new[] { "application_membership_id", "role_code", "scope_type", "scope_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_roles_company_status_until", "application_role_assignments", new[] { "company_id", "status", "valid_until" });
        migrationBuilder.CreateIndex("ix_app_entitlements_membership", "application_entitlements", "application_membership_id");
        migrationBuilder.CreateIndex("ix_app_entitlements_course_map", "application_entitlements", "application_course_map_id");
        migrationBuilder.CreateIndex("ix_app_entitlements_class_map", "application_entitlements", "application_class_map_id");
        migrationBuilder.CreateIndex("ix_app_entitlements_student", "application_entitlements", "csca_class_student_id");
        migrationBuilder.CreateIndex("ix_app_entitlements_membership_course_class", "application_entitlements", new[] { "application_membership_id", "application_course_map_id", "application_class_map_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_app_entitlements_company_status_until", "application_entitlements", new[] { "company_id", "status", "valid_until" });
        migrationBuilder.CreateIndex("ix_app_entitlements_student_course", "application_entitlements", new[] { "csca_class_student_id", "application_course_map_id" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "application_entitlements");
        migrationBuilder.DropTable(name: "application_role_assignments");
        migrationBuilder.DropTable(name: "application_class_maps");
        migrationBuilder.DropTable(name: "application_memberships");
        migrationBuilder.DropTable(name: "application_course_maps");
        migrationBuilder.DropTable(name: "managed_applications");
    }
}
