using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260902150000_AddOwnerAssignmentAnchor")]
public partial class AddOwnerAssignmentAnchor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "OwnerAssignedAt",
            table: "sales_customers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"sales_customers\" SET \"OwnerAssignedAt\" = COALESCE(\"SourceCreatedAt\", CURRENT_TIMESTAMP) WHERE \"OwnerAssignedAt\" IS NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_sales_customers_TenantId_OwnerAssignedAt",
            table: "sales_customers",
            columns: new[] { "TenantId", "OwnerAssignedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_sales_customers_TenantId_OwnerAssignedAt",
            table: "sales_customers");

        migrationBuilder.DropColumn(
            name: "OwnerAssignedAt",
            table: "sales_customers");
    }
}
