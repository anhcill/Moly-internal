using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds display-friendly, multi-line supplier contacts. Each line is one phone
/// number or one bank account description, while the existing phone column is
/// retained as the legacy primary contact used by older integrations.
/// </summary>
public partial class AddSupplierMultiContactAndBankAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "phone_numbers",
            table: "suppliers",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "bank_accounts",
            table: "suppliers",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.Sql("UPDATE suppliers SET phone_numbers = phone WHERE phone IS NOT NULL AND phone <> '';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "phone_numbers", table: "suppliers");
        migrationBuilder.DropColumn(name: "bank_accounts", table: "suppliers");
    }
}
