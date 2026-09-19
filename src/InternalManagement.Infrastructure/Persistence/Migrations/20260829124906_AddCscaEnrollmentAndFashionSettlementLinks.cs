using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCscaEnrollmentAndFashionSettlementLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "returns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_document_id",
                table: "csca_class_students",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_settlements_business_documents_business_document_id",
                        column: x => x.business_document_id,
                        principalTable: "business_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_settlements_returns_return_id",
                        column: x => x.return_id,
                        principalTable: "returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_settlements_sales_documents_sales_document_id",
                        column: x => x.sales_document_id,
                        principalTable: "sales_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_settlements_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_returns_business_document_id",
                table: "returns",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_csca_class_students_business_document_id",
                table: "csca_class_students",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_settlements_business_document_id",
                table: "sales_settlements",
                column: "business_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_settlements_company_id_payment_reference",
                table: "sales_settlements",
                columns: new[] { "company_id", "payment_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_settlements_company_id_sales_order_id_status_occurred",
                table: "sales_settlements",
                columns: new[] { "company_id", "sales_order_id", "status", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_settlements_return_id",
                table: "sales_settlements",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_settlements_sales_document_id",
                table: "sales_settlements",
                column: "sales_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_settlements_sales_order_id",
                table: "sales_settlements",
                column: "sales_order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_csca_class_students_business_documents_business_document_id",
                table: "csca_class_students",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_returns_business_documents_business_document_id",
                table: "returns",
                column: "business_document_id",
                principalTable: "business_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_csca_class_students_business_documents_business_document_id",
                table: "csca_class_students");

            migrationBuilder.DropForeignKey(
                name: "fk_returns_business_documents_business_document_id",
                table: "returns");

            migrationBuilder.DropTable(
                name: "sales_settlements");

            migrationBuilder.DropIndex(
                name: "ix_returns_business_document_id",
                table: "returns");

            migrationBuilder.DropIndex(
                name: "ix_csca_class_students_business_document_id",
                table: "csca_class_students");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "returns");

            migrationBuilder.DropColumn(
                name: "business_document_id",
                table: "csca_class_students");
        }
    }
}
