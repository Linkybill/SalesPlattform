using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260902130000_NormalizeZohoActivityExternalIds")]
public partial class NormalizeZohoActivityExternalIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Older imports stored top-level activities without the module
        // prefix, while the deleted-record endpoint uses Tasks:<id>,
        // Calls:<id> and Emails:<id>. Normalize existing links/raw records
        // so old imported activities can be matched by the delete path.
        migrationBuilder.Sql(
            """
            UPDATE "integration_entity_links"
            SET "ExternalId" = substring("ExternalId" from 8)
            WHERE "InternalEntityType" = 'activity'
              AND ("ExternalId" LIKE 'Tasks:Tasks:%'
                OR "ExternalId" LIKE 'Calls:Calls:%'
                OR "ExternalId" LIKE 'Emails:Emails:%');
            """);

        migrationBuilder.Sql(
            """
            UPDATE "integration_raw_records"
            SET "ExternalId" = substring("ExternalId" from 8)
            WHERE "EntityType" = 'activity'
              AND ("ExternalId" LIKE 'Tasks:Tasks:%'
                OR "ExternalId" LIKE 'Calls:Calls:%'
                OR "ExternalId" LIKE 'Emails:Emails:%');
            """);

        migrationBuilder.Sql(
            """
            WITH activity_links AS (
                SELECT link."TenantId", link."ProviderKey", link."ConnectionKey", link."ExternalId",
                       activity."ActivityType"
                FROM "integration_entity_links" AS link
                INNER JOIN "sales_activities" AS activity
                    ON activity."TenantId" = link."TenantId"
                   AND activity."Id" = link."InternalEntityId"
                WHERE link."InternalEntityType" = 'activity'
                  AND activity."ActivityType" IN ('task', 'call', 'email')
                  AND link."ExternalId" NOT LIKE 'Tasks:%'
                  AND link."ExternalId" NOT LIKE 'Calls:%'
                  AND link."ExternalId" NOT LIKE 'Emails:%'
            )
            UPDATE "integration_entity_links" AS link
            SET "ExternalId" = CASE activity_links."ActivityType"
                WHEN 'task' THEN 'Tasks:' || link."ExternalId"
                WHEN 'call' THEN 'Calls:' || link."ExternalId"
                WHEN 'email' THEN 'Emails:' || link."ExternalId"
            END
            FROM activity_links
            WHERE link."TenantId" = activity_links."TenantId"
              AND link."ProviderKey" = activity_links."ProviderKey"
              AND link."ConnectionKey" = activity_links."ConnectionKey"
              AND link."ExternalId" = activity_links."ExternalId";
            """);

        migrationBuilder.Sql(
            """
            WITH activity_links AS (
                SELECT link."TenantId", link."ProviderKey", link."ConnectionKey",
                       link."ExternalId",
                       split_part(link."ExternalId", ':', 2) AS "LegacyExternalId"
                FROM "integration_entity_links" AS link
                INNER JOIN "sales_activities" AS activity
                    ON activity."TenantId" = link."TenantId"
                   AND activity."Id" = link."InternalEntityId"
                WHERE link."InternalEntityType" = 'activity'
                  AND activity."ActivityType" IN ('task', 'call', 'email')
                  AND (link."ExternalId" LIKE 'Tasks:%'
                    OR link."ExternalId" LIKE 'Calls:%'
                    OR link."ExternalId" LIKE 'Emails:%')
            )
            UPDATE "integration_raw_records" AS raw
            SET "ExternalId" = activity_links."ExternalId"
            FROM activity_links
            WHERE raw."TenantId" = activity_links."TenantId"
              AND raw."ProviderKey" = activity_links."ProviderKey"
              AND raw."ConnectionKey" = activity_links."ConnectionKey"
              AND raw."EntityType" = 'activity'
              AND raw."ExternalId" = activity_links."LegacyExternalId";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old format is ambiguous once Tasks and Calls share the same
        // canonical Activity entity type. Do not rewrite historical IDs back.
    }
}
