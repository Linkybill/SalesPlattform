using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260830130000_AddCrmIntegrationFoundation")]
public partial class AddCrmIntegrationFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "integration_connections" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "ConnectionKey" varchar(100) NOT NULL,
                "DisplayName" varchar(200) NOT NULL,
                "ExternalOrganizationId" varchar(200),
                "ApiDomain" varchar(300) NOT NULL,
                "EncryptedRefreshToken" text NOT NULL,
                "ConnectedAt" timestamptz NOT NULL,
                "LastTokenRefreshAt" timestamptz,
                "LastSyncAt" timestamptz,
                "IsActive" boolean NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_connections_TenantId_ProviderKey_ConnectionKey"
                ON "integration_connections" ("TenantId", "ProviderKey", "ConnectionKey");

            CREATE TABLE IF NOT EXISTS "integration_oauth_states" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "StateHash" varchar(128) NOT NULL,
                "UserSubject" varchar(256) NOT NULL,
                "ExpiresAt" timestamptz NOT NULL,
                "ConsumedAt" timestamptz
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_oauth_states_TenantId_StateHash"
                ON "integration_oauth_states" ("TenantId", "StateHash");
            CREATE INDEX IF NOT EXISTS "IX_integration_oauth_states_TenantId_ExpiresAt"
                ON "integration_oauth_states" ("TenantId", "ExpiresAt");

            CREATE TABLE IF NOT EXISTS "integration_entity_links" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "EntityType" varchar(80) NOT NULL,
                "ExternalId" varchar(200) NOT NULL,
                "InternalEntityType" varchar(80) NOT NULL,
                "InternalEntityId" uuid NOT NULL,
                "LastSeenAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_entity_links_TenantId_ProviderKey_EntityType_ExternalId"
                ON "integration_entity_links" ("TenantId", "ProviderKey", "EntityType", "ExternalId");
            CREATE INDEX IF NOT EXISTS "IX_integration_entity_links_TenantId_InternalEntityType_InternalEntityId"
                ON "integration_entity_links" ("TenantId", "InternalEntityType", "InternalEntityId");

            CREATE TABLE IF NOT EXISTS "integration_raw_records" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "EntityType" varchar(80) NOT NULL,
                "ExternalId" varchar(200) NOT NULL,
                "PayloadJson" jsonb NOT NULL,
                "ExternalModifiedAt" timestamptz,
                "SyncedAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_raw_records_TenantId_ProviderKey_EntityType_ExternalId"
                ON "integration_raw_records" ("TenantId", "ProviderKey", "EntityType", "ExternalId");
            CREATE INDEX IF NOT EXISTS "IX_integration_raw_records_TenantId_SyncedAt"
                ON "integration_raw_records" ("TenantId", "SyncedAt");

            CREATE TABLE IF NOT EXISTS "integration_sync_runs" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "Mode" varchar(30) NOT NULL,
                "Status" varchar(30) NOT NULL,
                "StartedAt" timestamptz NOT NULL,
                "FinishedAt" timestamptz,
                "RecordsRead" integer NOT NULL,
                "RecordsWritten" integer NOT NULL,
                "RecordsFailed" integer NOT NULL,
                "Error" varchar(4000)
            );
            CREATE INDEX IF NOT EXISTS "IX_integration_sync_runs_TenantId_StartedAt"
                ON "integration_sync_runs" ("TenantId", "StartedAt");

            CREATE TABLE IF NOT EXISTS "integration_sync_cursors" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "EntityType" varchar(80) NOT NULL,
                "LastModifiedAt" timestamptz,
                "LastExternalId" varchar(200),
                "UpdatedAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_sync_cursors_TenantId_ProviderKey_EntityType"
                ON "integration_sync_cursors" ("TenantId", "ProviderKey", "EntityType");

            CREATE TABLE IF NOT EXISTS "sales_customers" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Name" varchar(300) NOT NULL,
                "Industry" varchar(200),
                "PostalCode" varchar(30),
                "City" varchar(200),
                "Country" varchar(100),
                "OwnerExternalId" varchar(200),
                "Status" varchar(100),
                "SourceCreatedAt" timestamptz,
                "SourceModifiedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_customers_TenantId_Name"
                ON "sales_customers" ("TenantId", "Name");

            CREATE TABLE IF NOT EXISTS "sales_contacts" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "CustomerId" uuid,
                "Name" varchar(300) NOT NULL,
                "Email" varchar(320),
                "Phone" varchar(100),
                "JobTitle" varchar(200),
                "SourceModifiedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_contacts_TenantId_Email"
                ON "sales_contacts" ("TenantId", "Email");

            CREATE TABLE IF NOT EXISTS "sales_leads" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Name" varchar(300) NOT NULL,
                "CompanyName" varchar(300),
                "Email" varchar(320),
                "Phone" varchar(100),
                "Status" varchar(100),
                "Source" varchar(150),
                "LastContactAt" timestamptz,
                "CallAttempts" integer NOT NULL,
                "OwnerExternalId" varchar(200),
                "SourceCreatedAt" timestamptz,
                "SourceModifiedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_leads_TenantId_Email"
                ON "sales_leads" ("TenantId", "Email");
            CREATE INDEX IF NOT EXISTS "IX_sales_leads_TenantId_LastContactAt"
                ON "sales_leads" ("TenantId", "LastContactAt");

            CREATE TABLE IF NOT EXISTS "sales_products" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Name" varchar(300) NOT NULL,
                "Category" varchar(200),
                "SourceModifiedAt" timestamptz
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_products_TenantId_Name"
                ON "sales_products" ("TenantId", "Name");

            CREATE TABLE IF NOT EXISTS "sales_pipelines" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL,
                "Name" varchar(200) NOT NULL,
                "IsActive" boolean NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_pipelines_TenantId_Key"
                ON "sales_pipelines" ("TenantId", "Key");

            CREATE TABLE IF NOT EXISTS "sales_pipeline_stages" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "PipelineId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL,
                "Name" varchar(200) NOT NULL,
                "SortOrder" integer NOT NULL,
                "Probability" numeric(10,2)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_pipeline_stages_TenantId_PipelineId_Key"
                ON "sales_pipeline_stages" ("TenantId", "PipelineId", "Key");

            CREATE TABLE IF NOT EXISTS "sales_deals" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "CustomerId" uuid,
                "Name" varchar(300) NOT NULL,
                "Amount" numeric(18,2),
                "Currency" varchar(10),
                "PipelineKey" varchar(100),
                "StageKey" varchar(150),
                "ProductName" varchar(300),
                "DurationMonths" numeric(10,2),
                "ContractEndAt" timestamptz,
                "ClosingAt" timestamptz,
                "Status" varchar(100),
                "LossReason" varchar(300),
                "OwnerExternalId" varchar(200),
                "LastActivityAt" timestamptz,
                "SourceCreatedAt" timestamptz,
                "SourceModifiedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_deals_TenantId_CustomerId"
                ON "sales_deals" ("TenantId", "CustomerId");
            CREATE INDEX IF NOT EXISTS "IX_sales_deals_TenantId_ClosingAt"
                ON "sales_deals" ("TenantId", "ClosingAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_deals_TenantId_StageKey"
                ON "sales_deals" ("TenantId", "StageKey");

            CREATE TABLE IF NOT EXISTS "sales_deal_stage_history" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "DealId" uuid NOT NULL,
                "StageKey" varchar(150) NOT NULL,
                "EnteredAt" timestamptz NOT NULL,
                "ExitedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_deal_stage_history_TenantId_DealId_EnteredAt"
                ON "sales_deal_stage_history" ("TenantId", "DealId", "EnteredAt");

            CREATE TABLE IF NOT EXISTS "sales_activities" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ActivityType" varchar(100) NOT NULL,
                "Subject" varchar(500),
                "OccurredAt" timestamptz NOT NULL,
                "DurationSeconds" integer,
                "Direction" varchar(50),
                "Result" varchar(200),
                "OwnerExternalId" varchar(200),
                "RelatedEntityType" varchar(80),
                "RelatedExternalId" varchar(200),
                "SourceModifiedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_activities_TenantId_OccurredAt"
                ON "sales_activities" ("TenantId", "OccurredAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_activities_TenantId_RelatedEntityType_RelatedExternalId"
                ON "sales_activities" ("TenantId", "RelatedEntityType", "RelatedExternalId");

            CREATE TABLE IF NOT EXISTS "sales_appointments" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Subject" varchar(500),
                "StartsAt" timestamptz NOT NULL,
                "EndsAt" timestamptz NOT NULL,
                "Status" varchar(100) NOT NULL,
                "AppointmentType" varchar(150),
                "OwnerExternalId" varchar(200),
                "RelatedEntityType" varchar(80),
                "RelatedExternalId" varchar(200),
                "SourceModifiedAt" timestamptz
            );
            CREATE INDEX IF NOT EXISTS "IX_sales_appointments_TenantId_StartsAt"
                ON "sales_appointments" ("TenantId", "StartsAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "sales_appointments" CASCADE;
            DROP TABLE IF EXISTS "sales_activities" CASCADE;
            DROP TABLE IF EXISTS "sales_deal_stage_history" CASCADE;
            DROP TABLE IF EXISTS "sales_deals" CASCADE;
            DROP TABLE IF EXISTS "sales_pipeline_stages" CASCADE;
            DROP TABLE IF EXISTS "sales_pipelines" CASCADE;
            DROP TABLE IF EXISTS "sales_products" CASCADE;
            DROP TABLE IF EXISTS "sales_leads" CASCADE;
            DROP TABLE IF EXISTS "sales_contacts" CASCADE;
            DROP TABLE IF EXISTS "sales_customers" CASCADE;
            DROP TABLE IF EXISTS "integration_sync_cursors" CASCADE;
            DROP TABLE IF EXISTS "integration_sync_runs" CASCADE;
            DROP TABLE IF EXISTS "integration_raw_records" CASCADE;
            DROP TABLE IF EXISTS "integration_entity_links" CASCADE;
            DROP TABLE IF EXISTS "integration_oauth_states" CASCADE;
            DROP TABLE IF EXISTS "integration_connections" CASCADE;
            """);
    }
}
