using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

public partial class CompleteSalesDomainModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- The foundation migration already owns these tables. Extend them
            -- without dropping existing tenant data.
            ALTER TABLE IF EXISTS "integration_entity_links"
                ADD COLUMN IF NOT EXISTS "ConnectionKey" varchar(100) NOT NULL DEFAULT 'default';
            ALTER TABLE IF EXISTS "integration_entity_links"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;
            ALTER TABLE IF EXISTS "integration_raw_records"
                ADD COLUMN IF NOT EXISTS "ConnectionKey" varchar(100) NOT NULL DEFAULT 'default';
            ALTER TABLE IF EXISTS "integration_raw_records"
                ADD COLUMN IF NOT EXISTS "FirstSeenAt" timestamptz NOT NULL DEFAULT now();
            ALTER TABLE IF EXISTS "integration_raw_records"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz NOT NULL DEFAULT now();
            ALTER TABLE IF EXISTS "integration_raw_records"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;
            ALTER TABLE IF EXISTS "integration_raw_records"
                ADD COLUMN IF NOT EXISTS "SyncRunId" uuid;
            ALTER TABLE IF EXISTS "integration_raw_records"
                ADD COLUMN IF NOT EXISTS "SyncedAt" timestamptz NOT NULL DEFAULT now();

            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "ConnectionKey" varchar(100) NOT NULL DEFAULT 'default';
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "RequestedModulesJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "RequestedBy" varchar(256);
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "QueuedAt" timestamptz NOT NULL DEFAULT now();
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ALTER COLUMN "StartedAt" DROP NOT NULL;
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "CurrentModule" varchar(100);
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0;
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "LeaseUntil" timestamptz;
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "WorkerId" varchar(200);
            ALTER TABLE IF EXISTS "integration_sync_runs"
                ADD COLUMN IF NOT EXISTS "CorrelationId" varchar(200);

            ALTER TABLE IF EXISTS "integration_sync_cursors"
                ADD COLUMN IF NOT EXISTS "ConnectionKey" varchar(100) NOT NULL DEFAULT 'default';
            ALTER TABLE IF EXISTS "integration_sync_cursors"
                ADD COLUMN IF NOT EXISTS "LastSuccessfulRunId" uuid;
            ALTER TABLE IF EXISTS "integration_sync_cursors"
                ADD COLUMN IF NOT EXISTS "LastStartedAt" timestamptz;
            ALTER TABLE IF EXISTS "integration_sync_cursors"
                ADD COLUMN IF NOT EXISTS "LastError" varchar(4000);

            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "LegalName" varchar(300);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "TaxNumber" varchar(100);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "WebsiteDomain" varchar(300);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "RegionCode" varchar(100);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "CountryCode" varchar(10);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "AddressLine1" varchar(300);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "HouseNumber" varchar(50);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "OwnerId" uuid;
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "LastContactAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "LastPhoneCallAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "LifetimeRevenue" numeric(18,2);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "NeedsReview" boolean NOT NULL DEFAULT false;
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "GeocodingStatus" varchar(40);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "Latitude" numeric(9,6);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "Longitude" numeric(9,6);
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_customers"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;
            UPDATE "sales_customers" SET "Status" = 'unknown' WHERE "Status" IS NULL;
            ALTER TABLE IF EXISTS "sales_customers"
                ALTER COLUMN "Status" SET DEFAULT 'unknown',
                ALTER COLUMN "Status" SET NOT NULL;

            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "FirstName" varchar(150);
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "LastName" varchar(150);
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "NormalizedEmail" varchar(320);
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "NormalizedPhone" varchar(100);
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "MobilePhone" varchar(100);
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "IsPrimary" boolean NOT NULL DEFAULT false;
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "SourceCreatedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;

            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "CustomerId" uuid;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "ContactId" uuid;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "OwnerId" uuid;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "NormalizedEmail" varchar(320);
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "NormalizedPhone" varchar(100);
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "LastPhoneCallAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "ResponseDueAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "CallsSinceConversation" integer NOT NULL DEFAULT 0;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "TotalCallAttempts" integer NOT NULL DEFAULT 0;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "FirstActivityAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "NeedsReview" boolean NOT NULL DEFAULT false;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_leads"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;
            UPDATE "sales_leads" SET "Status" = 'new' WHERE "Status" IS NULL;
            ALTER TABLE IF EXISTS "sales_leads"
                ALTER COLUMN "Status" SET DEFAULT 'new',
                ALTER COLUMN "Status" SET NOT NULL;

            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "CategoryId" uuid;
            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "Key" varchar(100);
            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "Description" varchar(2000);
            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "SourceCreatedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_products"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;
            UPDATE "sales_products"
                SET "Key" = left('legacy-' || "Id"::text, 100)
                WHERE "Key" IS NULL OR "Key" = '';
            ALTER TABLE IF EXISTS "sales_products"
                ALTER COLUMN "Key" SET NOT NULL;

            ALTER TABLE IF EXISTS "sales_pipelines"
                ADD COLUMN IF NOT EXISTS "Description" varchar(2000);
            ALTER TABLE IF EXISTS "sales_pipelines"
                ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;
            ALTER TABLE IF EXISTS "sales_pipelines"
                ADD COLUMN IF NOT EXISTS "SourceCreatedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_pipelines"
                ADD COLUMN IF NOT EXISTS "SourceModifiedAt" timestamptz;

            ALTER TABLE IF EXISTS "sales_pipeline_stages"
                ADD COLUMN IF NOT EXISTS "StageType" varchar(30) NOT NULL DEFAULT 'open';
            ALTER TABLE IF EXISTS "sales_pipeline_stages"
                ADD COLUMN IF NOT EXISTS "IsTerminal" boolean NOT NULL DEFAULT false;
            ALTER TABLE IF EXISTS "sales_pipeline_stages"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_pipeline_stages"
                ADD COLUMN IF NOT EXISTS "SourceModifiedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_pipeline_stages"
                ALTER COLUMN "Probability" TYPE numeric(5,4)
                USING CASE WHEN "Probability" > 1 THEN "Probability" / 100 ELSE "Probability" END;

            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "OwnerId" uuid;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "PipelineId" uuid;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "PipelineStageId" uuid;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "ProductId" uuid;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "ContractStartAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "NeedsReview" boolean NOT NULL DEFAULT false;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_deals"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;
            UPDATE "sales_deals" SET "Status" = 'open' WHERE "Status" IS NULL;
            ALTER TABLE IF EXISTS "sales_deals"
                ALTER COLUMN "Status" SET DEFAULT 'open',
                ALTER COLUMN "Status" SET NOT NULL;

            ALTER TABLE IF EXISTS "sales_deal_stage_history"
                ADD COLUMN IF NOT EXISTS "PipelineId" uuid;
            ALTER TABLE IF EXISTS "sales_deal_stage_history"
                ADD COLUMN IF NOT EXISTS "PipelineStageId" uuid;
            ALTER TABLE IF EXISTS "sales_deal_stage_history"
                ADD COLUMN IF NOT EXISTS "StageKeySnapshot" varchar(150);
            ALTER TABLE IF EXISTS "sales_deal_stage_history"
                ADD COLUMN IF NOT EXISTS "SourceObservedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_deal_stage_history"
                ADD COLUMN IF NOT EXISTS "SourceEventKey" varchar(200);
            UPDATE "sales_deal_stage_history"
                SET "StageKeySnapshot" = "StageKey"
                WHERE "StageKeySnapshot" IS NULL;
            ALTER TABLE IF EXISTS "sales_deal_stage_history"
                ALTER COLUMN "StageKeySnapshot" SET NOT NULL;

            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "OwnerId" uuid;
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "ConnectionStatus" varchar(50);
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "ConversationClass" varchar(50);
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "CountsAsConversation" boolean;
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "IsCorrected" boolean NOT NULL DEFAULT false;
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "CorrectionNote" varchar(1000);
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "SourceCreatedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_activities"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;

            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "OwnerId" uuid;
            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "OriginalStartsAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "RescheduleCount" integer NOT NULL DEFAULT 0;
            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "SourceCreatedAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamptz;
            ALTER TABLE IF EXISTS "sales_appointments"
                ADD COLUMN IF NOT EXISTS "SourceDeletedAt" timestamptz;

            -- New integration tables.
            CREATE TABLE IF NOT EXISTS "integration_sync_run_items" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "SyncRunId" uuid NOT NULL,
                "Module" varchar(100) NOT NULL,
                "Status" varchar(30) NOT NULL,
                "Cursor" varchar(500),
                "StartedAt" timestamptz,
                "FinishedAt" timestamptz,
                "RecordsRead" integer NOT NULL,
                "RecordsWritten" integer NOT NULL,
                "RecordsFailed" integer NOT NULL,
                "Error" varchar(4000)
            );
            CREATE TABLE IF NOT EXISTS "integration_sync_errors" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "SyncRunId" uuid NOT NULL,
                "SyncRunItemId" uuid,
                "Module" varchar(100) NOT NULL,
                "ExternalId" varchar(200),
                "ErrorCode" varchar(100) NOT NULL,
                "Message" varchar(4000) NOT NULL,
                "Retryable" boolean NOT NULL,
                "Attempt" integer NOT NULL,
                "OccurredAt" timestamptz NOT NULL,
                "DetailsJson" jsonb
            );
            CREATE TABLE IF NOT EXISTS "integration_field_mappings" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "ConnectionKey" varchar(100) NOT NULL,
                "SourceEntityType" varchar(100) NOT NULL,
                "SourceField" varchar(200) NOT NULL,
                "TargetEntityType" varchar(100) NOT NULL,
                "TargetField" varchar(200) NOT NULL,
                "TransformationKey" varchar(150),
                "IsRequired" boolean NOT NULL,
                "ConfigurationJson" jsonb,
                "Version" integer NOT NULL,
                "IsActive" boolean NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "integration_pipeline_mappings" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "ConnectionKey" varchar(100) NOT NULL,
                "ExternalPipelineId" varchar(200) NOT NULL,
                "InternalPipelineId" uuid NOT NULL,
                "SourceNameSnapshot" varchar(300),
                "IsActive" boolean NOT NULL,
                "LastSeenAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "integration_stage_mappings" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "ConnectionKey" varchar(100) NOT NULL,
                "ExternalPipelineId" varchar(200) NOT NULL,
                "ExternalStageId" varchar(200) NOT NULL,
                "InternalPipelineId" uuid NOT NULL,
                "InternalStageId" uuid NOT NULL,
                "SourceNameSnapshot" varchar(300),
                "SourceProbability" numeric(5,4),
                "IsActive" boolean NOT NULL,
                "LastSeenAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "integration_writeback_operations" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "ConnectionKey" varchar(100) NOT NULL,
                "EntityType" varchar(100) NOT NULL,
                "InternalEntityId" uuid NOT NULL,
                "ExternalId" varchar(200),
                "OperationType" varchar(50) NOT NULL,
                "Status" varchar(30) NOT NULL,
                "PayloadJson" jsonb,
                "RequestedAt" timestamptz NOT NULL,
                "CompletedAt" timestamptz,
                "Error" varchar(4000)
            );
            CREATE TABLE IF NOT EXISTS "integration_webhook_events" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProviderKey" varchar(50) NOT NULL,
                "ConnectionKey" varchar(100) NOT NULL,
                "EventType" varchar(150) NOT NULL,
                "ExternalEventId" varchar(200),
                "PayloadJson" jsonb NOT NULL,
                "Status" varchar(30) NOT NULL,
                "AttemptCount" integer NOT NULL,
                "ReceivedAt" timestamptz NOT NULL,
                "ProcessedAt" timestamptz,
                "Error" varchar(4000)
            );

            -- New canonical CRM-neutral tables.
            CREATE TABLE IF NOT EXISTS "sales_owners" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "DisplayName" varchar(300) NOT NULL, "Email" varchar(320),
                "IsActive" boolean NOT NULL, "SourceCreatedAt" timestamptz,
                "SourceModifiedAt" timestamptz, "LastSeenAt" timestamptz,
                "SourceDeletedAt" timestamptz
            );
            CREATE TABLE IF NOT EXISTS "sales_teams" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL, "Name" varchar(200) NOT NULL,
                "IsActive" boolean NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_team_members" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "TeamId" uuid NOT NULL, "OwnerId" uuid NOT NULL,
                "ValidFrom" timestamptz NOT NULL, "ValidTo" timestamptz,
                "IsPrimary" boolean NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_customer_relationships" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "ParentCustomerId" uuid NOT NULL, "ChildCustomerId" uuid NOT NULL,
                "RelationshipType" varchar(80) NOT NULL, "ValidFrom" timestamptz,
                "ValidTo" timestamptz, "Source" varchar(100), "Notes" varchar(2000)
            );
            CREATE TABLE IF NOT EXISTS "sales_customer_status_history" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "CustomerId" uuid NOT NULL, "Status" varchar(100) NOT NULL,
                "ValidFrom" timestamptz NOT NULL, "ValidTo" timestamptz,
                "SourceModifiedAt" timestamptz
            );
            CREATE TABLE IF NOT EXISTS "sales_product_categories" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL, "Name" varchar(200) NOT NULL,
                "IsActive" boolean NOT NULL, "SortOrder" integer NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_contracts" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "CustomerId" uuid NOT NULL, "DealId" uuid, "ProductId" uuid,
                "OwnerId" uuid, "ContractNumber" varchar(150), "Status" varchar(50) NOT NULL,
                "StartAt" timestamptz, "EndAt" timestamptz,
                "DurationMonths" numeric(10,2), "RecurringAmount" numeric(18,2),
                "Currency" varchar(10), "IsActive" boolean NOT NULL,
                "SourceModifiedAt" timestamptz, "LastSeenAt" timestamptz,
                "SourceDeletedAt" timestamptz
            );
            CREATE TABLE IF NOT EXISTS "sales_activity_relations" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "ActivityId" uuid NOT NULL, "TargetType" varchar(50) NOT NULL,
                "TargetId" uuid NOT NULL, "RelationRole" varchar(50)
            );
            CREATE TABLE IF NOT EXISTS "sales_appointment_relations" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "AppointmentId" uuid NOT NULL, "TargetType" varchar(50) NOT NULL,
                "TargetId" uuid NOT NULL, "RelationRole" varchar(50)
            );
            CREATE TABLE IF NOT EXISTS "sales_appointment_status_history" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "AppointmentId" uuid NOT NULL, "Status" varchar(100) NOT NULL,
                "ChangedAt" timestamptz NOT NULL, "OriginalStartsAt" timestamptz,
                "Source" varchar(100), "Notes" varchar(1000)
            );

            -- Workflow, rules, targets and calendar tables.
            CREATE TABLE IF NOT EXISTS "sales_work_items" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "WorkItemType" varchar(60) NOT NULL, "Status" varchar(40) NOT NULL,
                "Title" varchar(500) NOT NULL, "Reason" text, "OwnerId" uuid,
                "DueAt" timestamptz, "PriorityScore" numeric(10,2),
                "PriorityCalculatedAt" timestamptz, "SourceRuleCode" varchar(50),
                "SourceRuleRunId" uuid, "RequiresApproval" boolean NOT NULL,
                "CompletedAt" timestamptz, "CompletedBy" varchar(256),
                "DismissedAt" timestamptz, "SnoozedUntil" timestamptz,
                "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_work_item_relations" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "WorkItemId" uuid NOT NULL, "TargetType" varchar(50) NOT NULL,
                "TargetId" uuid NOT NULL, "RelationRole" varchar(50)
            );
            CREATE TABLE IF NOT EXISTS "sales_work_item_events" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "WorkItemId" uuid NOT NULL, "EventType" varchar(60) NOT NULL,
                "DetailsJson" jsonb, "ActorSubject" varchar(256),
                "OccurredAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_rule_definitions" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Code" varchar(50) NOT NULL, "Name" varchar(200) NOT NULL,
                "Description" varchar(2000), "IsEnabled" boolean NOT NULL,
                "AutomationMode" varchar(50) NOT NULL, "Version" integer NOT NULL,
                "ParametersJson" jsonb, "ValidFrom" timestamptz, "ValidTo" timestamptz,
                "UpdatedBy" varchar(256), "UpdatedAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_rule_runs" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "TriggerType" varchar(50) NOT NULL, "Status" varchar(30) NOT NULL,
                "StartedAt" timestamptz NOT NULL, "FinishedAt" timestamptz,
                "RuleSetVersion" integer NOT NULL, "EvaluatedCount" integer NOT NULL,
                "CreatedCount" integer NOT NULL, "Error" varchar(4000)
            );
            CREATE TABLE IF NOT EXISTS "sales_rule_evaluations" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "RuleRunId" uuid NOT NULL, "RuleDefinitionId" uuid NOT NULL,
                "TargetType" varchar(50) NOT NULL, "TargetId" uuid NOT NULL,
                "Outcome" varchar(50) NOT NULL, "WorkItemId" uuid,
                "ExplanationJson" jsonb, "EvaluatedAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_priority_profiles" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL, "Name" varchar(200) NOT NULL,
                "Description" varchar(2000), "IsActive" boolean NOT NULL,
                "BaseScore" numeric(10,2) NOT NULL, "AgeBonusPerDay" numeric(10,2) NOT NULL,
                "ValueBonusFactor" numeric(10,4) NOT NULL, "MaximumScore" numeric(10,2),
                "ValidFrom" timestamptz, "ValidTo" timestamptz,
                "UpdatedBy" varchar(256), "UpdatedAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_priority_weights" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "PriorityProfileId" uuid NOT NULL, "WorkItemType" varchar(60) NOT NULL,
                "Weight" numeric(10,4) NOT NULL, "ConfigurationJson" jsonb
            );
            CREATE TABLE IF NOT EXISTS "sales_fiscal_years" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Name" varchar(100) NOT NULL, "StartsAt" date NOT NULL,
                "EndsAt" date NOT NULL, "TimeZone" varchar(100) NOT NULL,
                "IsClosed" boolean NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_target_periods" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL, "PeriodType" varchar(30) NOT NULL,
                "PeriodNumber" integer NOT NULL, "StartsAt" date NOT NULL,
                "EndsAt" date NOT NULL, "DistributionWeight" numeric(7,4) NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_targets" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL, "TargetPeriodId" uuid,
                "OwnerId" uuid NOT NULL, "TargetType" varchar(60) NOT NULL,
                "AppointmentType" varchar(150), "TargetValue" numeric(18,2) NOT NULL,
                "Currency" varchar(10), "ApprovedAt" timestamptz,
                "ApprovedBy" varchar(256), "ValidFrom" date NOT NULL, "ValidTo" date
            );
            CREATE TABLE IF NOT EXISTS "sales_work_calendars" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL, "Name" varchar(200) NOT NULL,
                "TimeZone" varchar(100) NOT NULL, "IsDefault" boolean NOT NULL,
                "IsActive" boolean NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_working_hours" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "CalendarId" uuid NOT NULL, "DayOfWeek" integer NOT NULL,
                "IsWorkingDay" boolean NOT NULL, "StartAt" time,
                "EndAt" time, "BreakStartAt" time, "BreakEndAt" time
            );
            CREATE TABLE IF NOT EXISTS "sales_holidays" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "CalendarId" uuid NOT NULL, "Date" date NOT NULL,
                "Name" varchar(200) NOT NULL, "IsWorkingDayOverride" boolean NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_communication_templates" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Key" varchar(100) NOT NULL, "Name" varchar(200) NOT NULL,
                "Channel" varchar(50) NOT NULL, "SubjectTemplate" varchar(1000),
                "BodyTemplate" text NOT NULL, "IsActive" boolean NOT NULL,
                "Version" integer NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                "UpdatedBy" varchar(256)
            );
            CREATE TABLE IF NOT EXISTS "sales_notifications" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "RecipientSubject" varchar(256) NOT NULL, "WorkItemId" uuid,
                "Title" varchar(500), "PayloadJson" jsonb, "DueAt" timestamptz,
                "EscalationLevel" integer NOT NULL, "DeliveryStatus" varchar(30) NOT NULL,
                "SentAt" timestamptz, "ReadAt" timestamptz, "CreatedAt" timestamptz NOT NULL
            );

            -- Snapshot, data quality, duplicate and audit tables.
            CREATE TABLE IF NOT EXISTS "sales_snapshot_runs" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "SnapshotDate" date NOT NULL, "SnapshotType" varchar(50) NOT NULL,
                "Status" varchar(30) NOT NULL, "StartedAt" timestamptz NOT NULL,
                "FinishedAt" timestamptz, "Error" varchar(4000)
            );
            CREATE TABLE IF NOT EXISTS "sales_pipeline_snapshots" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "SnapshotRunId" uuid NOT NULL, "SnapshotDate" date NOT NULL,
                "PipelineId" uuid NOT NULL, "PipelineStageId" uuid NOT NULL,
                "OwnerId" uuid, "OpenDealCount" bigint NOT NULL,
                "OpenAmount" numeric(18,2), "WeightedAmount" numeric(18,2),
                "Currency" varchar(10)
            );
            CREATE TABLE IF NOT EXISTS "sales_kpi_snapshots" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "SnapshotRunId" uuid NOT NULL, "SnapshotDate" date NOT NULL,
                "PeriodType" varchar(20) NOT NULL, "PeriodStart" date NOT NULL,
                "PeriodEnd" date NOT NULL, "MetricKey" varchar(100) NOT NULL,
                "OwnerId" uuid, "PipelineId" uuid, "ProductCategoryId" uuid,
                "Industry" varchar(200), "CountryCode" varchar(10),
                "PostalRegion" varchar(10), "Value" numeric(20,4),
                "CountValue" bigint, "Numerator" numeric(20,4),
                "Denominator" numeric(20,4), "Currency" varchar(10), "DetailsJson" jsonb
            );
            CREATE TABLE IF NOT EXISTS "sales_activity_snapshots" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "SnapshotRunId" uuid NOT NULL, "SnapshotDate" date NOT NULL,
                "PeriodStart" date NOT NULL, "PeriodEnd" date NOT NULL,
                "OwnerId" uuid, "ActivityType" varchar(100),
                "PlannedCount" bigint NOT NULL, "CompletedCount" bigint NOT NULL,
                "CancelledCount" bigint NOT NULL, "RescheduledCount" bigint NOT NULL,
                "NoShowCount" bigint NOT NULL, "ReachedCallCount" bigint NOT NULL,
                "UnreachedCallCount" bigint NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "sales_customer_status_snapshots" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "SnapshotRunId" uuid NOT NULL, "SnapshotDate" date NOT NULL,
                "PeriodStart" date NOT NULL, "PeriodEnd" date NOT NULL,
                "Status" varchar(100) NOT NULL, "ActiveCount" bigint NOT NULL,
                "AddedCount" bigint NOT NULL, "LostCount" bigint NOT NULL,
                "LifetimeRevenue" numeric(18,2)
            );
            CREATE TABLE IF NOT EXISTS "sales_data_quality_findings" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "Code" varchar(100) NOT NULL, "Severity" varchar(30) NOT NULL,
                "Status" varchar(30) NOT NULL, "EntityType" varchar(100) NOT NULL,
                "EntityId" uuid, "FieldName" varchar(200), "Message" varchar(2000) NOT NULL,
                "DetailsJson" jsonb, "Fingerprint" varchar(256) NOT NULL,
                "DetectedAt" timestamptz NOT NULL, "LastDetectedAt" timestamptz,
                "ResolvedAt" timestamptz, "ResolvedBy" varchar(256)
            );
            CREATE TABLE IF NOT EXISTS "sales_duplicate_candidates" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "CustomerAId" uuid NOT NULL, "CustomerBId" uuid NOT NULL,
                "Score" numeric(10,4) NOT NULL, "Confidence" varchar(30) NOT NULL,
                "MatchDetailsJson" jsonb, "Status" varchar(30) NOT NULL,
                "DetectedAt" timestamptz NOT NULL, "ResolvedAt" timestamptz,
                "ResolvedBy" varchar(256)
            );
            CREATE TABLE IF NOT EXISTS "sales_duplicate_decisions" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "DuplicateCandidateId" uuid NOT NULL, "Decision" varchar(50) NOT NULL,
                "DecidedBy" varchar(256) NOT NULL, "DecidedAt" timestamptz NOT NULL,
                "LeadingCustomerId" uuid, "FieldSelectionsJson" jsonb, "Notes" varchar(2000)
            );
            CREATE TABLE IF NOT EXISTS "sales_merge_operations" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "DuplicateCandidateId" uuid, "SourceCustomerId" uuid NOT NULL,
                "TargetCustomerId" uuid NOT NULL, "Status" varchar(30) NOT NULL,
                "ApprovedBy" varchar(256), "ApprovedAt" timestamptz,
                "StartedAt" timestamptz, "CompletedAt" timestamptz,
                "TransferredDealCount" integer NOT NULL, "TransferredActivityCount" integer NOT NULL,
                "TransferredAppointmentCount" integer NOT NULL,
                "WritebackReference" varchar(300), "Error" varchar(4000)
            );
            CREATE TABLE IF NOT EXISTS "sales_owner_change_requests" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "TargetType" varchar(50) NOT NULL, "TargetId" uuid NOT NULL,
                "CustomerId" uuid, "OldOwnerId" uuid, "ProposedOwnerId" uuid,
                "SourceRuleCode" varchar(50), "Reason" varchar(2000) NOT NULL,
                "Status" varchar(30) NOT NULL, "RequestedAt" timestamptz NOT NULL,
                "DecidedAt" timestamptz, "DecidedBy" varchar(256),
                "AppliedAt" timestamptz, "WritebackStatus" varchar(30)
            );
            CREATE TABLE IF NOT EXISTS "sales_audit_log" (
                "Id" uuid NOT NULL PRIMARY KEY, "TenantId" uuid NOT NULL,
                "ActorSubject" varchar(256), "ActorDisplayName" varchar(300),
                "Action" varchar(100) NOT NULL, "EntityType" varchar(100) NOT NULL,
                "EntityId" uuid, "OccurredAt" timestamptz NOT NULL,
                "BeforeJson" jsonb, "AfterJson" jsonb, "CorrelationId" varchar(200)
            );

            -- Tenant-local uniqueness and query indexes.
            DROP INDEX IF EXISTS "IX_integration_entity_links_TenantId_ProviderKey_EntityType_ExternalId";
            DROP INDEX IF EXISTS "IX_integration_raw_records_TenantId_ProviderKey_EntityType_ExternalId";
            DROP INDEX IF EXISTS "IX_integration_sync_cursors_TenantId_ProviderKey_EntityType";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_entity_links_TenantId_ProviderKey_ConnectionKey_EntityType_ExternalId"
                ON "integration_entity_links" ("TenantId", "ProviderKey", "ConnectionKey", "EntityType", "ExternalId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_raw_records_TenantId_ProviderKey_ConnectionKey_EntityType_ExternalId"
                ON "integration_raw_records" ("TenantId", "ProviderKey", "ConnectionKey", "EntityType", "ExternalId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_sync_cursors_TenantId_ProviderKey_ConnectionKey_EntityType"
                ON "integration_sync_cursors" ("TenantId", "ProviderKey", "ConnectionKey", "EntityType");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_sync_run_items_TenantId_SyncRunId_Module"
                ON "integration_sync_run_items" ("TenantId", "SyncRunId", "Module");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_field_mappings_TenantId_Key"
                ON "integration_field_mappings" ("TenantId", "ProviderKey", "ConnectionKey", "SourceEntityType", "SourceField", "TargetEntityType", "TargetField", "Version");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_pipeline_mappings_TenantId_Key"
                ON "integration_pipeline_mappings" ("TenantId", "ProviderKey", "ConnectionKey", "ExternalPipelineId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_stage_mappings_TenantId_Key"
                ON "integration_stage_mappings" ("TenantId", "ProviderKey", "ConnectionKey", "ExternalPipelineId", "ExternalStageId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_integration_webhook_events_TenantId_Key"
                ON "integration_webhook_events" ("TenantId", "ProviderKey", "ConnectionKey", "ExternalEventId");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_teams_TenantId_Key" ON "sales_teams" ("TenantId", "Key");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_product_categories_TenantId_Key" ON "sales_product_categories" ("TenantId", "Key");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_products_TenantId_Key" ON "sales_products" ("TenantId", "Key");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_contracts_TenantId_ContractNumber" ON "sales_contracts" ("TenantId", "ContractNumber");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_activity_relations_TenantId_Key" ON "sales_activity_relations" ("TenantId", "ActivityId", "TargetType", "TargetId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_appointment_relations_TenantId_Key" ON "sales_appointment_relations" ("TenantId", "AppointmentId", "TargetType", "TargetId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_rule_definitions_TenantId_Code_Version" ON "sales_rule_definitions" ("TenantId", "Code", "Version");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_priority_profiles_TenantId_Key" ON "sales_priority_profiles" ("TenantId", "Key");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_priority_weights_TenantId_Key" ON "sales_priority_weights" ("TenantId", "PriorityProfileId", "WorkItemType");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_fiscal_years_TenantId_Name" ON "sales_fiscal_years" ("TenantId", "Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_target_periods_TenantId_Key" ON "sales_target_periods" ("TenantId", "FiscalYearId", "PeriodType", "PeriodNumber");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_targets_TenantId_Key" ON "sales_targets" ("TenantId", "OwnerId", "FiscalYearId", "TargetType", "TargetPeriodId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_work_calendars_TenantId_Key" ON "sales_work_calendars" ("TenantId", "Key");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_working_hours_TenantId_Key" ON "sales_working_hours" ("TenantId", "CalendarId", "DayOfWeek");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_holidays_TenantId_Key" ON "sales_holidays" ("TenantId", "CalendarId", "Date");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_communication_templates_TenantId_Key" ON "sales_communication_templates" ("TenantId", "Key", "Version");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_snapshot_runs_TenantId_Key" ON "sales_snapshot_runs" ("TenantId", "SnapshotDate", "SnapshotType");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_pipeline_snapshots_TenantId_Key" ON "sales_pipeline_snapshots" ("TenantId", "SnapshotDate", "PipelineId", "PipelineStageId", "OwnerId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_kpi_snapshots_TenantId_Key" ON "sales_kpi_snapshots" ("TenantId", "SnapshotDate", "MetricKey", "PeriodType", "OwnerId", "PipelineId", "ProductCategoryId", "Industry", "CountryCode", "PostalRegion");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_activity_snapshots_TenantId_Key" ON "sales_activity_snapshots" ("TenantId", "SnapshotDate", "PeriodStart", "PeriodEnd", "OwnerId", "ActivityType");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_customer_status_snapshots_TenantId_Key" ON "sales_customer_status_snapshots" ("TenantId", "SnapshotDate", "PeriodStart", "PeriodEnd", "Status");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_data_quality_findings_TenantId_Fingerprint" ON "sales_data_quality_findings" ("TenantId", "Fingerprint");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_duplicate_candidates_TenantId_Key" ON "sales_duplicate_candidates" ("TenantId", "CustomerAId", "CustomerBId");

            CREATE INDEX IF NOT EXISTS "IX_sales_work_items_TenantId_Status_DueAt" ON "sales_work_items" ("TenantId", "Status", "DueAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_work_items_TenantId_OwnerId_Status" ON "sales_work_items" ("TenantId", "OwnerId", "Status");
            CREATE INDEX IF NOT EXISTS "IX_sales_work_items_TenantId_PriorityScore" ON "sales_work_items" ("TenantId", "PriorityScore");
            CREATE INDEX IF NOT EXISTS "IX_sales_deals_TenantId_PipelineId_PipelineStageId" ON "sales_deals" ("TenantId", "PipelineId", "PipelineStageId");
            CREATE INDEX IF NOT EXISTS "IX_sales_deals_TenantId_OwnerId_Status" ON "sales_deals" ("TenantId", "OwnerId", "Status");
            CREATE INDEX IF NOT EXISTS "IX_sales_activities_TenantId_OwnerId_OccurredAt" ON "sales_activities" ("TenantId", "OwnerId", "OccurredAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_appointments_TenantId_OwnerId_StartsAt" ON "sales_appointments" ("TenantId", "OwnerId", "StartsAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_data_quality_findings_TenantId_Status_Severity" ON "sales_data_quality_findings" ("TenantId", "Status", "Severity");
            CREATE INDEX IF NOT EXISTS "IX_sales_duplicate_candidates_TenantId_Status_Score" ON "sales_duplicate_candidates" ("TenantId", "Status", "Score");
            CREATE INDEX IF NOT EXISTS "IX_sales_audit_log_TenantId_Entity_OccurredAt" ON "sales_audit_log" ("TenantId", "EntityType", "EntityId", "OccurredAt");

            -- Foreign keys are added idempotently so this migration also works
            -- against databases created by the original SQL foundation.
            DO $$ BEGIN
                ALTER TABLE "integration_raw_records" ADD CONSTRAINT "FK_integration_raw_records_sync_run"
                    FOREIGN KEY ("SyncRunId") REFERENCES "integration_sync_runs" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "integration_sync_run_items" ADD CONSTRAINT "FK_integration_sync_run_items_sync_run"
                    FOREIGN KEY ("SyncRunId") REFERENCES "integration_sync_runs" ("Id") ON DELETE CASCADE;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "integration_sync_errors" ADD CONSTRAINT "FK_integration_sync_errors_sync_run"
                    FOREIGN KEY ("SyncRunId") REFERENCES "integration_sync_runs" ("Id") ON DELETE CASCADE;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "integration_sync_errors" ADD CONSTRAINT "FK_integration_sync_errors_sync_run_item"
                    FOREIGN KEY ("SyncRunItemId") REFERENCES "integration_sync_run_items" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "integration_sync_cursors" ADD CONSTRAINT "FK_integration_sync_cursors_last_run"
                    FOREIGN KEY ("LastSuccessfulRunId") REFERENCES "integration_sync_runs" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "integration_pipeline_mappings" ADD CONSTRAINT "FK_integration_pipeline_mappings_pipeline"
                    FOREIGN KEY ("InternalPipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "integration_stage_mappings" ADD CONSTRAINT "FK_integration_stage_mappings_pipeline"
                    FOREIGN KEY ("InternalPipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "integration_stage_mappings" ADD CONSTRAINT "FK_integration_stage_mappings_stage"
                    FOREIGN KEY ("InternalStageId") REFERENCES "sales_pipeline_stages" ("Id") ON DELETE RESTRICT;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;

            DO $$ BEGIN
                ALTER TABLE "sales_team_members" ADD CONSTRAINT "FK_sales_team_members_team" FOREIGN KEY ("TeamId") REFERENCES "sales_teams" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_team_members" ADD CONSTRAINT "FK_sales_team_members_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE RESTRICT;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_customers" ADD CONSTRAINT "FK_sales_customers_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_customer_relationships" ADD CONSTRAINT "FK_sales_customer_relationships_parent" FOREIGN KEY ("ParentCustomerId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_customer_relationships" ADD CONSTRAINT "FK_sales_customer_relationships_child" FOREIGN KEY ("ChildCustomerId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_customer_status_history" ADD CONSTRAINT "FK_sales_customer_status_history_customer" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_contacts" ADD CONSTRAINT "FK_sales_contacts_customer" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_leads" ADD CONSTRAINT "FK_sales_leads_customer" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_leads" ADD CONSTRAINT "FK_sales_leads_contact" FOREIGN KEY ("ContactId") REFERENCES "sales_contacts" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_leads" ADD CONSTRAINT "FK_sales_leads_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_products" ADD CONSTRAINT "FK_sales_products_category" FOREIGN KEY ("CategoryId") REFERENCES "sales_product_categories" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_pipeline_stages" ADD CONSTRAINT "FK_sales_pipeline_stages_pipeline" FOREIGN KEY ("PipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_deals" ADD CONSTRAINT "FK_sales_deals_customer" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_deals" ADD CONSTRAINT "FK_sales_deals_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_deals" ADD CONSTRAINT "FK_sales_deals_pipeline" FOREIGN KEY ("PipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_deals" ADD CONSTRAINT "FK_sales_deals_stage" FOREIGN KEY ("PipelineStageId") REFERENCES "sales_pipeline_stages" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_deals" ADD CONSTRAINT "FK_sales_deals_product" FOREIGN KEY ("ProductId") REFERENCES "sales_products" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_contracts" ADD CONSTRAINT "FK_sales_contracts_customer" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_contracts" ADD CONSTRAINT "FK_sales_contracts_deal" FOREIGN KEY ("DealId") REFERENCES "sales_deals" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_contracts" ADD CONSTRAINT "FK_sales_contracts_product" FOREIGN KEY ("ProductId") REFERENCES "sales_products" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_contracts" ADD CONSTRAINT "FK_sales_contracts_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_deal_stage_history" ADD CONSTRAINT "FK_sales_deal_stage_history_deal" FOREIGN KEY ("DealId") REFERENCES "sales_deals" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_deal_stage_history" ADD CONSTRAINT "FK_sales_deal_stage_history_pipeline" FOREIGN KEY ("PipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_deal_stage_history" ADD CONSTRAINT "FK_sales_deal_stage_history_stage" FOREIGN KEY ("PipelineStageId") REFERENCES "sales_pipeline_stages" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_activities" ADD CONSTRAINT "FK_sales_activities_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_activity_relations" ADD CONSTRAINT "FK_sales_activity_relations_activity" FOREIGN KEY ("ActivityId") REFERENCES "sales_activities" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_appointments" ADD CONSTRAINT "FK_sales_appointments_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_appointment_relations" ADD CONSTRAINT "FK_sales_appointment_relations_appointment" FOREIGN KEY ("AppointmentId") REFERENCES "sales_appointments" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_appointment_status_history" ADD CONSTRAINT "FK_sales_appointment_status_history_appointment" FOREIGN KEY ("AppointmentId") REFERENCES "sales_appointments" ("Id") ON DELETE RESTRICT;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_work_items" ADD CONSTRAINT "FK_sales_work_items_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_work_items" ADD CONSTRAINT "FK_sales_work_items_rule_run" FOREIGN KEY ("SourceRuleRunId") REFERENCES "sales_rule_runs" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_work_item_relations" ADD CONSTRAINT "FK_sales_work_item_relations_work_item" FOREIGN KEY ("WorkItemId") REFERENCES "sales_work_items" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_work_item_events" ADD CONSTRAINT "FK_sales_work_item_events_work_item" FOREIGN KEY ("WorkItemId") REFERENCES "sales_work_items" ("Id") ON DELETE CASCADE;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_rule_evaluations" ADD CONSTRAINT "FK_sales_rule_evaluations_run" FOREIGN KEY ("RuleRunId") REFERENCES "sales_rule_runs" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_rule_evaluations" ADD CONSTRAINT "FK_sales_rule_evaluations_definition" FOREIGN KEY ("RuleDefinitionId") REFERENCES "sales_rule_definitions" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_rule_evaluations" ADD CONSTRAINT "FK_sales_rule_evaluations_work_item" FOREIGN KEY ("WorkItemId") REFERENCES "sales_work_items" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_priority_weights" ADD CONSTRAINT "FK_sales_priority_weights_profile" FOREIGN KEY ("PriorityProfileId") REFERENCES "sales_priority_profiles" ("Id") ON DELETE CASCADE;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_target_periods" ADD CONSTRAINT "FK_sales_target_periods_fiscal_year" FOREIGN KEY ("FiscalYearId") REFERENCES "sales_fiscal_years" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_targets" ADD CONSTRAINT "FK_sales_targets_fiscal_year" FOREIGN KEY ("FiscalYearId") REFERENCES "sales_fiscal_years" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_targets" ADD CONSTRAINT "FK_sales_targets_period" FOREIGN KEY ("TargetPeriodId") REFERENCES "sales_target_periods" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_targets" ADD CONSTRAINT "FK_sales_targets_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_working_hours" ADD CONSTRAINT "FK_sales_working_hours_calendar" FOREIGN KEY ("CalendarId") REFERENCES "sales_work_calendars" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_holidays" ADD CONSTRAINT "FK_sales_holidays_calendar" FOREIGN KEY ("CalendarId") REFERENCES "sales_work_calendars" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_notifications" ADD CONSTRAINT "FK_sales_notifications_work_item" FOREIGN KEY ("WorkItemId") REFERENCES "sales_work_items" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_pipeline_snapshots" ADD CONSTRAINT "FK_sales_pipeline_snapshots_run" FOREIGN KEY ("SnapshotRunId") REFERENCES "sales_snapshot_runs" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_pipeline_snapshots" ADD CONSTRAINT "FK_sales_pipeline_snapshots_pipeline" FOREIGN KEY ("PipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_pipeline_snapshots" ADD CONSTRAINT "FK_sales_pipeline_snapshots_stage" FOREIGN KEY ("PipelineStageId") REFERENCES "sales_pipeline_stages" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_pipeline_snapshots" ADD CONSTRAINT "FK_sales_pipeline_snapshots_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_kpi_snapshots" ADD CONSTRAINT "FK_sales_kpi_snapshots_run" FOREIGN KEY ("SnapshotRunId") REFERENCES "sales_snapshot_runs" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_kpi_snapshots" ADD CONSTRAINT "FK_sales_kpi_snapshots_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_kpi_snapshots" ADD CONSTRAINT "FK_sales_kpi_snapshots_pipeline" FOREIGN KEY ("PipelineId") REFERENCES "sales_pipelines" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_kpi_snapshots" ADD CONSTRAINT "FK_sales_kpi_snapshots_category" FOREIGN KEY ("ProductCategoryId") REFERENCES "sales_product_categories" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_activity_snapshots" ADD CONSTRAINT "FK_sales_activity_snapshots_run" FOREIGN KEY ("SnapshotRunId") REFERENCES "sales_snapshot_runs" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_activity_snapshots" ADD CONSTRAINT "FK_sales_activity_snapshots_owner" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_customer_status_snapshots" ADD CONSTRAINT "FK_sales_customer_status_snapshots_run" FOREIGN KEY ("SnapshotRunId") REFERENCES "sales_snapshot_runs" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_duplicate_candidates" ADD CONSTRAINT "FK_sales_duplicate_candidates_customer_a" FOREIGN KEY ("CustomerAId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_duplicate_candidates" ADD CONSTRAINT "FK_sales_duplicate_candidates_customer_b" FOREIGN KEY ("CustomerBId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            DO $$ BEGIN
                ALTER TABLE "sales_duplicate_decisions" ADD CONSTRAINT "FK_sales_duplicate_decisions_candidate" FOREIGN KEY ("DuplicateCandidateId") REFERENCES "sales_duplicate_candidates" ("Id") ON DELETE CASCADE;
                ALTER TABLE "sales_duplicate_decisions" ADD CONSTRAINT "FK_sales_duplicate_decisions_leading_customer" FOREIGN KEY ("LeadingCustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_merge_operations" ADD CONSTRAINT "FK_sales_merge_operations_candidate" FOREIGN KEY ("DuplicateCandidateId") REFERENCES "sales_duplicate_candidates" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_merge_operations" ADD CONSTRAINT "FK_sales_merge_operations_source" FOREIGN KEY ("SourceCustomerId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_merge_operations" ADD CONSTRAINT "FK_sales_merge_operations_target" FOREIGN KEY ("TargetCustomerId") REFERENCES "sales_customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE "sales_owner_change_requests" ADD CONSTRAINT "FK_sales_owner_change_requests_customer" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_owner_change_requests" ADD CONSTRAINT "FK_sales_owner_change_requests_old_owner" FOREIGN KEY ("OldOwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
                ALTER TABLE "sales_owner_change_requests" ADD CONSTRAINT "FK_sales_owner_change_requests_new_owner" FOREIGN KEY ("ProposedOwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL;
            EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This migration intentionally does not drop tenant data. A rollback
        // must be performed as an explicit, separately reviewed data migration.
    }
}
