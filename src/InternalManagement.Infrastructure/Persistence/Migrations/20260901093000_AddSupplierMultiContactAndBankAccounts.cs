using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds display-friendly, multi-line supplier contacts. Each line is one phone
/// number or one bank account description, while the existing phone column is
/// retained as the legacy primary contact used by older integrations.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901093000_AddSupplierMultiContactAndBankAccounts")]
public partial class AddSupplierMultiContactAndBankAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Older installations received these columns outside EF migration history.
        migrationBuilder.Sql("""
            ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS phone_numbers character varying(4000);
            ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS bank_accounts character varying(4000);
            UPDATE suppliers SET phone_numbers = phone
            WHERE (phone_numbers IS NULL OR phone_numbers = '') AND phone IS NOT NULL AND phone <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "phone_numbers", table: "suppliers");
        migrationBuilder.DropColumn(name: "bank_accounts", table: "suppliers");
    }
}
