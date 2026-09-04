using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260903131000_AddCrmApiUsageOriginMetadata")]
public partial class AddCrmApiUsageOriginMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Origin",
            table: "integration_api_usage_events",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "unknown");

        migrationBuilder.AddColumn<string>(
            name: "RequestedBy",
            table: "integration_api_usage_events",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            table: "integration_api_usage_events",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_integration_api_usage_events_TenantId_Origin_OccurredAt",
            table: "integration_api_usage_events",
            columns: new[] { "TenantId", "Origin", "OccurredAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_integration_api_usage_events_TenantId_Origin_OccurredAt",
            table: "integration_api_usage_events");

        migrationBuilder.DropColumn(
            name: "Origin",
            table: "integration_api_usage_events");

        migrationBuilder.DropColumn(
            name: "RequestedBy",
            table: "integration_api_usage_events");

        migrationBuilder.DropColumn(
            name: "CorrelationId",
            table: "integration_api_usage_events");
    }
}
