using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260903110000_AddZohoSchemaCache")]
public partial class AddZohoSchemaCache : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "zoho_schema_cache",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ConnectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AvailableModulesJson = table.Column<string>(type: "jsonb", nullable: false),
                FieldsJson = table.Column<string>(type: "jsonb", nullable: false),
                ExternalOrganizationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_zoho_schema_cache", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_zoho_schema_cache_TenantId_ProviderKey_ConnectionKey",
            table: "zoho_schema_cache",
            columns: new[] { "TenantId", "ProviderKey", "ConnectionKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "zoho_schema_cache");
    }
}
