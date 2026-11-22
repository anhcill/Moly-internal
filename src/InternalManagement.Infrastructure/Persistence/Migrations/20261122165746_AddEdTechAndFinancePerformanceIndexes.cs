using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEdTechAndFinancePerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_question_banks_company_id_name",
                table: "question_banks",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_finance_transactions_company_id_business_unit_id_transactio",
                table: "finance_transactions",
                columns: new[] { "company_id", "business_unit_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_finance_transactions_company_id_reference_type_reference_id",
                table: "finance_transactions",
                columns: new[] { "company_id", "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ed_tech_customers_company_id_email",
                table: "ed_tech_customers",
                columns: new[] { "company_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_courses_company_id_business_unit_id",
                table: "courses",
                columns: new[] { "company_id", "business_unit_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_question_banks_company_id_name",
                table: "question_banks");

            migrationBuilder.DropIndex(
                name: "ix_finance_transactions_company_id_business_unit_id_transactio",
                table: "finance_transactions");

            migrationBuilder.DropIndex(
                name: "ix_finance_transactions_company_id_reference_type_reference_id",
                table: "finance_transactions");

            migrationBuilder.DropIndex(
                name: "ix_ed_tech_customers_company_id_email",
                table: "ed_tech_customers");

            migrationBuilder.DropIndex(
                name: "ix_courses_company_id_business_unit_id",
                table: "courses");
        }
    }
}
