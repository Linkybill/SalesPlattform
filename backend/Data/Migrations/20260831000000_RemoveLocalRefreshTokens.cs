using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

public partial class RemoveLocalRefreshTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EncryptedRefreshToken",
            table: "integration_connections");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "EncryptedRefreshToken",
            table: "integration_connections",
            type: "text",
            nullable: false,
            defaultValue: "");
    }
}
