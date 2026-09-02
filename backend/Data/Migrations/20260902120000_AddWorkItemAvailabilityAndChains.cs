using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260902120000_AddWorkItemAvailabilityAndChains")]
public partial class AddWorkItemAvailabilityAndChains : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AvailableFrom",
            table: "sales_work_items",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ClosureReason",
            table: "sales_work_items",
            type: "character varying(60)",
            maxLength: 60,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "PreviousWorkItemId",
            table: "sales_work_items",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "WorkItemChainId",
            table: "sales_work_items",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"sales_work_items\" SET \"WorkItemChainId\" = \"Id\" WHERE \"WorkItemChainId\" IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "WorkItemChainId",
            table: "sales_work_items",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_work_items_TenantId_Status_AvailableFrom",
            table: "sales_work_items",
            columns: new[] { "TenantId", "Status", "AvailableFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_work_items_TenantId_WorkItemChainId_CreatedAt",
            table: "sales_work_items",
            columns: new[] { "TenantId", "WorkItemChainId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_work_items_TenantId_PreviousWorkItemId",
            table: "sales_work_items",
            columns: new[] { "TenantId", "PreviousWorkItemId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_sales_work_items_TenantId_Status_AvailableFrom",
            table: "sales_work_items");

        migrationBuilder.DropIndex(
            name: "IX_sales_work_items_TenantId_WorkItemChainId_CreatedAt",
            table: "sales_work_items");

        migrationBuilder.DropIndex(
            name: "IX_sales_work_items_TenantId_PreviousWorkItemId",
            table: "sales_work_items");

        migrationBuilder.DropColumn(
            name: "AvailableFrom",
            table: "sales_work_items");

        migrationBuilder.DropColumn(
            name: "ClosureReason",
            table: "sales_work_items");

        migrationBuilder.DropColumn(
            name: "PreviousWorkItemId",
            table: "sales_work_items");

        migrationBuilder.DropColumn(
            name: "WorkItemChainId",
            table: "sales_work_items");
    }
}
