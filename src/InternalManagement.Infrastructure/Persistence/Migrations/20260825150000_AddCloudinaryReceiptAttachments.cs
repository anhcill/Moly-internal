using System;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260825150000_AddCloudinaryReceiptAttachments")]
public partial class AddCloudinaryReceiptAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "entity_id",
            table: "documents",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "entity_type",
            table: "documents",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "storage_provider",
            table: "documents",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "MinIO");

        migrationBuilder.AddColumn<string>(
            name: "storage_public_id",
            table: "documents",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_documents_company_id_entity_type_entity_id",
            table: "documents",
            columns: new[] { "company_id", "entity_type", "entity_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_documents_company_id_entity_type_entity_id",
            table: "documents");

        migrationBuilder.DropColumn(name: "entity_id", table: "documents");
        migrationBuilder.DropColumn(name: "entity_type", table: "documents");
        migrationBuilder.DropColumn(name: "storage_provider", table: "documents");
        migrationBuilder.DropColumn(name: "storage_public_id", table: "documents");
    }
}
