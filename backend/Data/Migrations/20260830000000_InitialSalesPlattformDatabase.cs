using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260830000000_InitialSalesPlattformDatabase")]
public partial class InitialSalesPlattformDatabase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "application_settings",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                Scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ValueJson = table.Column<string>(type: "jsonb", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_application_settings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "hello_world_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hello_world_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_application_settings_TenantId_Key_Scope",
            table: "application_settings",
            columns: new[] { "TenantId", "Key", "Scope" },
            unique: true,
            filter: "\"UserId\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_application_settings_TenantId_Key_Scope_UserId",
            table: "application_settings",
            columns: new[] { "TenantId", "Key", "Scope", "UserId" },
            unique: true,
            filter: "\"UserId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_hello_world_records_CreatedAt",
            table: "hello_world_records",
            column: "CreatedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "application_settings");
        migrationBuilder.DropTable(name: "hello_world_records");
    }
}
