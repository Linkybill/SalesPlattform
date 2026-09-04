using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260903140000_ExpandZohoSchemaCache")]
public partial class ExpandZohoSchemaCache : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LayoutsJson",
            table: "zoho_schema_cache",
            type: "jsonb",
            nullable: false,
            defaultValue: "{}");

        migrationBuilder.AddColumn<string>(
            name: "PipelinesJson",
            table: "zoho_schema_cache",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "RelatedListsJson",
            table: "zoho_schema_cache",
            type: "jsonb",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LayoutsJson",
            table: "zoho_schema_cache");

        migrationBuilder.DropColumn(
            name: "PipelinesJson",
            table: "zoho_schema_cache");

        migrationBuilder.DropColumn(
            name: "RelatedListsJson",
            table: "zoho_schema_cache");
    }
}
