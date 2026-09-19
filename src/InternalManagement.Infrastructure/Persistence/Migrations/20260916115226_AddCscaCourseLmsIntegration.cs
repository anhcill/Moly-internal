using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaCourseLmsIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_outboxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_outboxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lms_account_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    csca_class_student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_student_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lms_user_id = table.Column<long>(type: "bigint", nullable: true),
                    lms_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    last_provisioned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_account_links", x => x.id);
                    table.CheckConstraint("ck_lms_account_links_identity", "party_id IS NOT NULL OR csca_class_student_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_lms_account_links_csca_class_students_csca_class_student_id",
                        column: x => x.csca_class_student_id,
                        principalTable: "csca_class_students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_lms_account_links_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "lms_course_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_course_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lms_course_id = table.Column<long>(type: "bigint", nullable: true),
                    lms_course_slug = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_course_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_lms_course_links_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lms_sync_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    last_correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_sync_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lms_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lms_account_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lms_course_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    csca_class_student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_grant_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    source_payment_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lms_enrollment_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_access_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_lms_access_grants_csca_class_students_csca_class_student_id",
                        column: x => x.csca_class_student_id,
                        principalTable: "csca_class_students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lms_access_grants_lms_account_links_lms_account_link_id",
                        column: x => x.lms_account_link_id,
                        principalTable: "lms_account_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lms_access_grants_lms_course_links_lms_course_link_id",
                        column: x => x.lms_course_link_id,
                        principalTable: "lms_course_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_outboxes_source_system_event_id",
                table: "integration_outboxes",
                columns: new[] { "source_system", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_outboxes_source_system_idempotency_key",
                table: "integration_outboxes",
                columns: new[] { "source_system", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_outboxes_source_system_status_next_attempt_at",
                table: "integration_outboxes",
                columns: new[] { "source_system", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lms_access_grants_company_id_source_system_external_grant_id",
                table: "lms_access_grants",
                columns: new[] { "company_id", "source_system", "external_grant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_access_grants_csca_class_student_id_lms_course_link_id",
                table: "lms_access_grants",
                columns: new[] { "csca_class_student_id", "lms_course_link_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_access_grants_lms_account_link_id_status_valid_until",
                table: "lms_access_grants",
                columns: new[] { "lms_account_link_id", "status", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_lms_access_grants_lms_course_link_id",
                table: "lms_access_grants",
                column: "lms_course_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_account_links_company_id_source_system_external_student",
                table: "lms_account_links",
                columns: new[] { "company_id", "source_system", "external_student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_account_links_company_id_source_system_lms_user_id",
                table: "lms_account_links",
                columns: new[] { "company_id", "source_system", "lms_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_account_links_company_id_source_system_party_id",
                table: "lms_account_links",
                columns: new[] { "company_id", "source_system", "party_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_account_links_company_id_status",
                table: "lms_account_links",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lms_account_links_csca_class_student_id",
                table: "lms_account_links",
                column: "csca_class_student_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_account_links_party_id",
                table: "lms_account_links",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_course_links_company_id_source_system_course_id",
                table: "lms_course_links",
                columns: new[] { "company_id", "source_system", "course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_course_links_company_id_source_system_external_course_id",
                table: "lms_course_links",
                columns: new[] { "company_id", "source_system", "external_course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_course_links_company_id_source_system_lms_course_id",
                table: "lms_course_links",
                columns: new[] { "company_id", "source_system", "lms_course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_course_links_company_id_status",
                table: "lms_course_links",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lms_course_links_course_id",
                table: "lms_course_links",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_sync_statuses_company_id_source_system_entity_type_exte",
                table: "lms_sync_statuses",
                columns: new[] { "company_id", "source_system", "entity_type", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_sync_statuses_company_id_source_system_status_last_sync",
                table: "lms_sync_statuses",
                columns: new[] { "company_id", "source_system", "status", "last_synced_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_outboxes");

            migrationBuilder.DropTable(
                name: "lms_access_grants");

            migrationBuilder.DropTable(
                name: "lms_sync_statuses");

            migrationBuilder.DropTable(
                name: "lms_account_links");

            migrationBuilder.DropTable(
                name: "lms_course_links");
        }
    }
}
