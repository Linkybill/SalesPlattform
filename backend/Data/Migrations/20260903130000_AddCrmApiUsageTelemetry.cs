using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260903130000_AddCrmApiUsageTelemetry")]
public partial class AddCrmApiUsageTelemetry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "integration_api_usage_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ConnectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: true),
                HttpMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                StatusCode = table.Column<int>(type: "integer", nullable: true),
                Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                Retryable = table.Column<bool>(type: "boolean", nullable: false),
                EstimatedUnits = table.Column<long>(type: "bigint", nullable: false),
                UsageUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ProviderUnitsRemaining = table.Column<int>(type: "integer", nullable: true),
                ProviderUnitsLimit = table.Column<int>(type: "integer", nullable: true),
                RecordsAffected = table.Column<int>(type: "integer", nullable: true),
                DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_integration_api_usage_events", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_integration_api_usage_events_TenantId_ProviderKey_ConnectionKey_OccurredAt",
            table: "integration_api_usage_events",
            columns: new[] { "TenantId", "ProviderKey", "ConnectionKey", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_integration_api_usage_events_TenantId_RunId_OccurredAt",
            table: "integration_api_usage_events",
            columns: new[] { "TenantId", "RunId", "OccurredAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "integration_api_usage_events");
    }
}
