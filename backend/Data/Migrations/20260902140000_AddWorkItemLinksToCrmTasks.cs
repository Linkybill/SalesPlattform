using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260902140000_AddWorkItemLinksToCrmTasks")]
public partial class AddWorkItemLinksToCrmTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "WorkItemId",
            table: "integration_entity_links",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_integration_entity_links_TenantId_WorkItemId",
            table: "integration_entity_links",
            columns: new[] { "TenantId", "WorkItemId" });

        migrationBuilder.AddForeignKey(
            name: "FK_integration_entity_links_sales_work_items_WorkItemId",
            table: "integration_entity_links",
            column: "WorkItemId",
            principalTable: "sales_work_items",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_integration_entity_links_sales_work_items_WorkItemId",
            table: "integration_entity_links");

        migrationBuilder.DropIndex(
            name: "IX_integration_entity_links_TenantId_WorkItemId",
            table: "integration_entity_links");

        migrationBuilder.DropColumn(
            name: "WorkItemId",
            table: "integration_entity_links");
    }
}
