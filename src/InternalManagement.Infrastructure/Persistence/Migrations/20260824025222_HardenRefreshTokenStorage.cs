using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenRefreshTokenStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refresh tokens are bearer secrets. Existing rows contain the
            // raw token and cannot be safely converted without the client
            // secret, so invalidate all sessions before removing plaintext.
            migrationBuilder.Sql("DELETE FROM user_refresh_tokens;");

            migrationBuilder.DropIndex(
                name: "ix_user_refresh_tokens_token",
                table: "user_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "replaced_by_token",
                table: "user_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token",
                table: "user_refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "replaced_by_token_hash",
                table: "user_refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                table: "user_refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_user_refresh_tokens_token_hash",
                table: "user_refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_refresh_tokens_token_hash",
                table: "user_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "replaced_by_token_hash",
                table: "user_refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "user_refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "replaced_by_token",
                table: "user_refresh_tokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token",
                table: "user_refresh_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_user_refresh_tokens_token",
                table: "user_refresh_tokens",
                column: "token",
                unique: true);
        }
    }
}
