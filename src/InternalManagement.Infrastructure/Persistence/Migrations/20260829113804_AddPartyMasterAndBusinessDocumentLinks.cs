using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyMasterAndBusinessDocumentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                table: "suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "sales_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_party_id",
                table: "sales_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "sales_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_party_id",
                table: "sales_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "purchase_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "payroll_periods",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "interview_customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                table: "interview_customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                table: "internal_customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "finance_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                table: "ed_tech_customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                table: "csca_class_students",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("pk_parties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_source_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_documents_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "party_business_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_business_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_business_profiles_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "party_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_contacts_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "party_external_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_external_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_external_identities_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_document_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_document_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_document_links_business_documents_from_document_id",
                        column: x => x.from_document_id,
                        principalTable: "business_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_business_document_links_business_documents_to_document_id",
                        column: x => x.to_document_id,
                        principalTable: "business_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_party_id",
                table: "suppliers",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_business_document_id",
                table: "sales_orders",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_customer_party_id",
                table: "sales_orders",
                column: "customer_party_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_documents_business_document_id",
                table: "sales_documents",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_documents_customer_party_id",
                table: "sales_documents",
                column: "customer_party_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipts_business_document_id",
                table: "purchase_receipts",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_periods_business_document_id",
                table: "payroll_periods",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_business_document_id",
                table: "payments",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_customers_business_document_id",
                table: "interview_customers",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_customers_party_id",
                table: "interview_customers",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_internal_customers_party_id",
                table: "internal_customers",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_finance_transactions_business_document_id",
                table: "finance_transactions",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_ed_tech_customers_party_id",
                table: "ed_tech_customers",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_csca_class_students_party_id",
                table: "csca_class_students",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_document_links_from_document_id_to_document_id_lin",
                table: "business_document_links",
                columns: new[] { "from_document_id", "to_document_id", "link_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_document_links_to_document_id",
                table: "business_document_links",
                column: "to_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_documents_company_id_business_unit_id_issued_at",
                table: "business_documents",
                columns: new[] { "company_id", "business_unit_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_business_documents_company_id_document_type_source_entity_t",
                table: "business_documents",
                columns: new[] { "company_id", "document_type", "source_entity_type", "source_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_documents_party_id",
                table: "business_documents",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_parties_company_id_display_name",
                table: "parties",
                columns: new[] { "company_id", "display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_party_business_profiles_party_id_business_unit_id_role",
                table: "party_business_profiles",
                columns: new[] { "party_id", "business_unit_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_contacts_party_id_type_normalized_value",
                table: "party_contacts",
                columns: new[] { "party_id", "type", "normalized_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_external_identities_company_id_source_system_source_id",
                table: "party_external_identities",
                columns: new[] { "company_id", "source_system", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_external_identities_party_id",
                table: "party_external_identities",
                column: "party_id");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_class_students_parties_party_id",
                table: "csca_class_students",
                column: "party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_ed_tech_customers_parties_party_id",
                table: "ed_tech_customers",
                column: "party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_finance_transactions_business_documents_business_document_id",
                table: "finance_transactions",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_internal_customers_parties_party_id",
                table: "internal_customers",
                column: "party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_interview_customers_business_documents_business_document_id",
                table: "interview_customers",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_interview_customers_parties_party_id",
                table: "interview_customers",
                column: "party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_business_documents_business_document_id",
                table: "payments",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_payroll_periods_business_documents_business_document_id",
                table: "payroll_periods",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_receipts_business_documents_business_document_id",
                table: "purchase_receipts",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_documents_business_documents_business_document_id",
                table: "sales_documents",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_documents_parties_customer_party_id",
                table: "sales_documents",
                column: "customer_party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_business_documents_business_document_id",
                table: "sales_orders",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_parties_customer_party_id",
                table: "sales_orders",
                column: "customer_party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_suppliers_parties_party_id",
                table: "suppliers",
                column: "party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_csca_class_students_parties_party_id",
                table: "csca_class_students");

            migrationBuilder.DropForeignKey(
                name: "fk_ed_tech_customers_parties_party_id",
                table: "ed_tech_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_finance_transactions_business_documents_business_document_id",
                table: "finance_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_internal_customers_parties_party_id",
                table: "internal_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_interview_customers_business_documents_business_document_id",
                table: "interview_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_interview_customers_parties_party_id",
                table: "interview_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_business_documents_business_document_id",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_payroll_periods_business_documents_business_document_id",
                table: "payroll_periods");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_receipts_business_documents_business_document_id",
                table: "purchase_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_documents_business_documents_business_document_id",
                table: "sales_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_documents_parties_customer_party_id",
                table: "sales_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_business_documents_business_document_id",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_parties_customer_party_id",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_suppliers_parties_party_id",
                table: "suppliers");

            migrationBuilder.DropTable(
                name: "business_document_links");

            migrationBuilder.DropTable(
                name: "party_business_profiles");

            migrationBuilder.DropTable(
                name: "party_contacts");

            migrationBuilder.DropTable(
                name: "party_external_identities");

            migrationBuilder.DropTable(
                name: "business_documents");

            migrationBuilder.DropTable(
                name: "parties");

            migrationBuilder.DropIndex(
                name: "ix_suppliers_party_id",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_business_document_id",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_customer_party_id",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_documents_business_document_id",
                table: "sales_documents");

            migrationBuilder.DropIndex(
                name: "ix_sales_documents_customer_party_id",
                table: "sales_documents");

            migrationBuilder.DropIndex(
                name: "ix_purchase_receipts_business_document_id",
                table: "purchase_receipts");

            migrationBuilder.DropIndex(
                name: "ix_payroll_periods_business_document_id",
                table: "payroll_periods");

            migrationBuilder.DropIndex(
                name: "ix_payments_business_document_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_interview_customers_business_document_id",
                table: "interview_customers");

            migrationBuilder.DropIndex(
                name: "ix_interview_customers_party_id",
                table: "interview_customers");

            migrationBuilder.DropIndex(
                name: "ix_internal_customers_party_id",
                table: "internal_customers");

            migrationBuilder.DropIndex(
                name: "ix_finance_transactions_business_document_id",
                table: "finance_transactions");

            migrationBuilder.DropIndex(
                name: "ix_ed_tech_customers_party_id",
                table: "ed_tech_customers");

            migrationBuilder.DropIndex(
                name: "ix_csca_class_students_party_id",
                table: "csca_class_students");

            migrationBuilder.DropColumn(
                name: "party_id",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "customer_party_id",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "sales_documents");

            migrationBuilder.DropColumn(
                name: "customer_party_id",
                table: "sales_documents");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "purchase_receipts");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "payroll_periods");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "interview_customers");

            migrationBuilder.DropColumn(
                name: "party_id",
                table: "interview_customers");

            migrationBuilder.DropColumn(
                name: "party_id",
                table: "internal_customers");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "finance_transactions");

            migrationBuilder.DropColumn(
                name: "party_id",
                table: "ed_tech_customers");

            migrationBuilder.DropColumn(
                name: "party_id",
                table: "csca_class_students");
        }
    }
}
