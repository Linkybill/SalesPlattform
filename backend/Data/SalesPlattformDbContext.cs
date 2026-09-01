using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SalesPlattform.Backend.Data;

public sealed class SalesPlattformDbContext(DbContextOptions<SalesPlattformDbContext> options)
    : PlatformTenantDbContext<SalesPlattformDbContext>(options)
{
    public DbSet<HelloWorldRecord> HelloWorldRecords => Set<HelloWorldRecord>();
    public DbSet<ApplicationSettingValueEntity> ApplicationSettingValues => Set<ApplicationSettingValueEntity>();

    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationOAuthState> IntegrationOAuthStates => Set<IntegrationOAuthState>();
    public DbSet<IntegrationEntityLink> IntegrationEntityLinks => Set<IntegrationEntityLink>();
    public DbSet<IntegrationRawRecord> IntegrationRawRecords => Set<IntegrationRawRecord>();
    public DbSet<IntegrationSyncRun> IntegrationSyncRuns => Set<IntegrationSyncRun>();
    public DbSet<IntegrationSyncRunItem> IntegrationSyncRunItems => Set<IntegrationSyncRunItem>();
    public DbSet<IntegrationSyncError> IntegrationSyncErrors => Set<IntegrationSyncError>();
    public DbSet<IntegrationSyncCursor> IntegrationSyncCursors => Set<IntegrationSyncCursor>();
    public DbSet<IntegrationFieldMapping> IntegrationFieldMappings => Set<IntegrationFieldMapping>();
    public DbSet<IntegrationPipelineMapping> IntegrationPipelineMappings => Set<IntegrationPipelineMapping>();
    public DbSet<IntegrationStageMapping> IntegrationStageMappings => Set<IntegrationStageMapping>();
    public DbSet<IntegrationWritebackOperation> IntegrationWritebackOperations => Set<IntegrationWritebackOperation>();
    public DbSet<IntegrationWebhookEvent> IntegrationWebhookEvents => Set<IntegrationWebhookEvent>();

    public DbSet<SalesOwner> SalesOwners => Set<SalesOwner>();
    public DbSet<SalesTeam> SalesTeams => Set<SalesTeam>();
    public DbSet<SalesTeamMember> SalesTeamMembers => Set<SalesTeamMember>();
    public DbSet<SalesCustomer> SalesCustomers => Set<SalesCustomer>();
    public DbSet<SalesCustomerRelationship> SalesCustomerRelationships => Set<SalesCustomerRelationship>();
    public DbSet<SalesCustomerStatusHistory> SalesCustomerStatusHistories => Set<SalesCustomerStatusHistory>();
    public DbSet<SalesContact> SalesContacts => Set<SalesContact>();
    public DbSet<SalesLead> SalesLeads => Set<SalesLead>();
    public DbSet<SalesProductCategory> SalesProductCategories => Set<SalesProductCategory>();
    public DbSet<SalesProduct> SalesProducts => Set<SalesProduct>();
    public DbSet<SalesPipeline> SalesPipelines => Set<SalesPipeline>();
    public DbSet<SalesPipelineStage> SalesPipelineStages => Set<SalesPipelineStage>();
    public DbSet<SalesDeal> SalesDeals => Set<SalesDeal>();
    public DbSet<SalesContract> SalesContracts => Set<SalesContract>();
    public DbSet<SalesDealStageHistory> SalesDealStageHistory => Set<SalesDealStageHistory>();
    public DbSet<SalesActivity> SalesActivities => Set<SalesActivity>();
    public DbSet<SalesActivityRelation> SalesActivityRelations => Set<SalesActivityRelation>();
    public DbSet<SalesAppointment> SalesAppointments => Set<SalesAppointment>();
    public DbSet<SalesAppointmentRelation> SalesAppointmentRelations => Set<SalesAppointmentRelation>();
    public DbSet<SalesAppointmentStatusHistory> SalesAppointmentStatusHistories => Set<SalesAppointmentStatusHistory>();

    public DbSet<SalesWorkItem> SalesWorkItems => Set<SalesWorkItem>();
    public DbSet<SalesWorkItemRelation> SalesWorkItemRelations => Set<SalesWorkItemRelation>();
    public DbSet<SalesWorkItemEvent> SalesWorkItemEvents => Set<SalesWorkItemEvent>();
    public DbSet<SalesRuleDefinition> SalesRuleDefinitions => Set<SalesRuleDefinition>();
    public DbSet<SalesRuleRun> SalesRuleRuns => Set<SalesRuleRun>();
    public DbSet<SalesRuleEvaluation> SalesRuleEvaluations => Set<SalesRuleEvaluation>();
    public DbSet<SalesPriorityProfile> SalesPriorityProfiles => Set<SalesPriorityProfile>();
    public DbSet<SalesPriorityWeight> SalesPriorityWeights => Set<SalesPriorityWeight>();
    public DbSet<SalesFiscalYear> SalesFiscalYears => Set<SalesFiscalYear>();
    public DbSet<SalesTargetPeriod> SalesTargetPeriods => Set<SalesTargetPeriod>();
    public DbSet<SalesTarget> SalesTargets => Set<SalesTarget>();
    public DbSet<SalesWorkCalendar> SalesWorkCalendars => Set<SalesWorkCalendar>();
    public DbSet<SalesWorkingHours> SalesWorkingHours => Set<SalesWorkingHours>();
    public DbSet<SalesHoliday> SalesHolidays => Set<SalesHoliday>();
    public DbSet<SalesCommunicationTemplate> SalesCommunicationTemplates => Set<SalesCommunicationTemplate>();
    public DbSet<SalesNotification> SalesNotifications => Set<SalesNotification>();

    public DbSet<SalesSnapshotRun> SalesSnapshotRuns => Set<SalesSnapshotRun>();
    public DbSet<SalesKpiSnapshot> SalesKpiSnapshots => Set<SalesKpiSnapshot>();
    public DbSet<SalesPipelineSnapshot> SalesPipelineSnapshots => Set<SalesPipelineSnapshot>();
    public DbSet<SalesActivitySnapshot> SalesActivitySnapshots => Set<SalesActivitySnapshot>();
    public DbSet<SalesCustomerStatusSnapshot> SalesCustomerStatusSnapshots => Set<SalesCustomerStatusSnapshot>();
    public DbSet<SalesDataQualityFinding> SalesDataQualityFindings => Set<SalesDataQualityFinding>();
    public DbSet<SalesDuplicateCandidate> SalesDuplicateCandidates => Set<SalesDuplicateCandidate>();
    public DbSet<SalesDuplicateDecision> SalesDuplicateDecisions => Set<SalesDuplicateDecision>();
    public DbSet<SalesMergeOperation> SalesMergeOperations => Set<SalesMergeOperation>();
    public DbSet<SalesOwnerChangeRequest> SalesOwnerChangeRequests => Set<SalesOwnerChangeRequest>();
    public DbSet<SalesAuditLog> SalesAuditLogs => Set<SalesAuditLog>();

    protected override void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HelloWorldRecord>(entity =>
        {
            entity.ToTable("hello_world_records");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Message).HasMaxLength(500).IsRequired();
            entity.Property(record => record.CreatedAt).IsRequired();
            entity.HasIndex(record => new { record.TenantId, record.CreatedAt });
        });

        ConfigureIntegrationModel(modelBuilder);
        ConfigureCanonicalModel(modelBuilder);
        ConfigureWorkflowModel(modelBuilder);
        ConfigureAnalyticsModel(modelBuilder);
    }

    private static void ConfigureIntegrationModel(ModelBuilder modelBuilder)
    {
        var connection = Table<IntegrationConnection>(modelBuilder, "integration_connections");
        connection.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        connection.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        connection.Property(item => item.DisplayName).HasMaxLength(200).IsRequired();
        connection.Property(item => item.ExternalOrganizationId).HasMaxLength(200);
        connection.Property(item => item.ApiDomain).HasMaxLength(300).IsRequired();
        connection.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey }).IsUnique();

        var oauthState = Table<IntegrationOAuthState>(modelBuilder, "integration_oauth_states");
        oauthState.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        oauthState.Property(item => item.StateHash).HasMaxLength(128).IsRequired();
        oauthState.Property(item => item.UserSubject).HasMaxLength(256).IsRequired();
        oauthState.HasIndex(item => new { item.TenantId, item.StateHash }).IsUnique();
        oauthState.HasIndex(item => new { item.TenantId, item.ExpiresAt });

        var link = Table<IntegrationEntityLink>(modelBuilder, "integration_entity_links");
        link.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        link.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        link.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
        link.Property(item => item.ExternalId).HasMaxLength(200).IsRequired();
        link.Property(item => item.InternalEntityType).HasMaxLength(80).IsRequired();
        link.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.EntityType, item.ExternalId }).IsUnique();
        link.HasIndex(item => new { item.TenantId, item.InternalEntityType, item.InternalEntityId });

        var raw = Table<IntegrationRawRecord>(modelBuilder, "integration_raw_records");
        raw.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        raw.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        raw.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
        raw.Property(item => item.ExternalId).HasMaxLength(200).IsRequired();
        raw.Property(item => item.PayloadJson).HasColumnType("jsonb").IsRequired();
        raw.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.EntityType, item.ExternalId }).IsUnique();
        raw.HasIndex(item => new { item.TenantId, item.LastSeenAt });
        raw.HasOne(item => item.SyncRun)
            .WithMany(item => item.RawRecords)
            .HasForeignKey(item => item.SyncRunId)
            .OnDelete(DeleteBehavior.SetNull);

        var run = Table<IntegrationSyncRun>(modelBuilder, "integration_sync_runs");
        run.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        run.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        run.Property(item => item.Mode).HasMaxLength(30).IsRequired();
        run.Property(item => item.Status).HasMaxLength(30).IsRequired();
        run.Property(item => item.RequestedModulesJson).HasColumnType("jsonb").IsRequired();
        run.Property(item => item.RequestedBy).HasMaxLength(256);
        run.Property(item => item.CurrentModule).HasMaxLength(100);
        run.Property(item => item.WorkerId).HasMaxLength(200);
        run.Property(item => item.Error).HasMaxLength(4000);
        run.Property(item => item.CorrelationId).HasMaxLength(200);
        run.HasIndex(item => new { item.TenantId, item.Status, item.QueuedAt });
        run.HasIndex(item => new { item.TenantId, item.StartedAt });

        var runItem = Table<IntegrationSyncRunItem>(modelBuilder, "integration_sync_run_items");
        runItem.Property(item => item.Module).HasMaxLength(100).IsRequired();
        runItem.Property(item => item.Status).HasMaxLength(30).IsRequired();
        runItem.Property(item => item.Cursor).HasMaxLength(500);
        runItem.Property(item => item.Error).HasMaxLength(4000);
        runItem.HasIndex(item => new { item.TenantId, item.SyncRunId, item.Module }).IsUnique();
        runItem.HasOne(item => item.SyncRun)
            .WithMany(item => item.Items)
            .HasForeignKey(item => item.SyncRunId)
            .OnDelete(DeleteBehavior.Cascade);

        var syncError = Table<IntegrationSyncError>(modelBuilder, "integration_sync_errors");
        syncError.Property(item => item.Module).HasMaxLength(100).IsRequired();
        syncError.Property(item => item.ExternalId).HasMaxLength(200);
        syncError.Property(item => item.ErrorCode).HasMaxLength(100).IsRequired();
        syncError.Property(item => item.Message).HasMaxLength(4000).IsRequired();
        syncError.Property(item => item.DetailsJson).HasColumnType("jsonb");
        syncError.HasIndex(item => new { item.TenantId, item.SyncRunId, item.OccurredAt });
        syncError.HasOne(item => item.SyncRun)
            .WithMany(item => item.Errors)
            .HasForeignKey(item => item.SyncRunId)
            .OnDelete(DeleteBehavior.Cascade);
        syncError.HasOne(item => item.SyncRunItem)
            .WithMany(item => item.Errors)
            .HasForeignKey(item => item.SyncRunItemId)
            .OnDelete(DeleteBehavior.SetNull);

        var cursor = Table<IntegrationSyncCursor>(modelBuilder, "integration_sync_cursors");
        cursor.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        cursor.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        cursor.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
        cursor.Property(item => item.LastExternalId).HasMaxLength(200);
        cursor.Property(item => item.LastError).HasMaxLength(4000);
        cursor.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.EntityType }).IsUnique();
        cursor.HasOne(item => item.LastSuccessfulRun)
            .WithMany()
            .HasForeignKey(item => item.LastSuccessfulRunId)
            .OnDelete(DeleteBehavior.SetNull);

        var fieldMapping = Table<IntegrationFieldMapping>(modelBuilder, "integration_field_mappings");
        fieldMapping.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        fieldMapping.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        fieldMapping.Property(item => item.SourceEntityType).HasMaxLength(100).IsRequired();
        fieldMapping.Property(item => item.SourceField).HasMaxLength(200).IsRequired();
        fieldMapping.Property(item => item.TargetEntityType).HasMaxLength(100).IsRequired();
        fieldMapping.Property(item => item.TargetField).HasMaxLength(200).IsRequired();
        fieldMapping.Property(item => item.TransformationKey).HasMaxLength(150);
        fieldMapping.Property(item => item.ConfigurationJson).HasColumnType("jsonb");
        fieldMapping.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.SourceEntityType, item.SourceField, item.TargetEntityType, item.TargetField, item.Version }).IsUnique();

        var pipelineMapping = Table<IntegrationPipelineMapping>(modelBuilder, "integration_pipeline_mappings");
        pipelineMapping.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        pipelineMapping.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        pipelineMapping.Property(item => item.ExternalPipelineId).HasMaxLength(200).IsRequired();
        pipelineMapping.Property(item => item.SourceNameSnapshot).HasMaxLength(300);
        pipelineMapping.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.ExternalPipelineId }).IsUnique();
        pipelineMapping.HasIndex(item => new { item.TenantId, item.InternalPipelineId });
        pipelineMapping.HasOne(item => item.InternalPipeline)
            .WithMany()
            .HasForeignKey(item => item.InternalPipelineId)
            .OnDelete(DeleteBehavior.Restrict);

        var stageMapping = Table<IntegrationStageMapping>(modelBuilder, "integration_stage_mappings");
        stageMapping.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        stageMapping.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        stageMapping.Property(item => item.ExternalPipelineId).HasMaxLength(200).IsRequired();
        stageMapping.Property(item => item.ExternalStageId).HasMaxLength(200).IsRequired();
        stageMapping.Property(item => item.SourceNameSnapshot).HasMaxLength(300);
        stageMapping.Property(item => item.SourceProbability).HasPrecision(5, 4);
        stageMapping.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.ExternalPipelineId, item.ExternalStageId }).IsUnique();
        stageMapping.HasIndex(item => new { item.TenantId, item.InternalStageId });
        stageMapping.HasOne(item => item.InternalPipeline)
            .WithMany()
            .HasForeignKey(item => item.InternalPipelineId)
            .OnDelete(DeleteBehavior.Restrict);
        stageMapping.HasOne(item => item.InternalStage)
            .WithMany()
            .HasForeignKey(item => item.InternalStageId)
            .OnDelete(DeleteBehavior.Restrict);

        var writeback = Table<IntegrationWritebackOperation>(modelBuilder, "integration_writeback_operations");
        writeback.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        writeback.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        writeback.Property(item => item.EntityType).HasMaxLength(100).IsRequired();
        writeback.Property(item => item.ExternalId).HasMaxLength(200);
        writeback.Property(item => item.OperationType).HasMaxLength(50).IsRequired();
        writeback.Property(item => item.Status).HasMaxLength(30).IsRequired();
        writeback.Property(item => item.PayloadJson).HasColumnType("jsonb");
        writeback.Property(item => item.Error).HasMaxLength(4000);
        writeback.HasIndex(item => new { item.TenantId, item.Status, item.RequestedAt });

        var webhook = Table<IntegrationWebhookEvent>(modelBuilder, "integration_webhook_events");
        webhook.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
        webhook.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
        webhook.Property(item => item.EventType).HasMaxLength(150).IsRequired();
        webhook.Property(item => item.ExternalEventId).HasMaxLength(200);
        webhook.Property(item => item.PayloadJson).HasColumnType("jsonb").IsRequired();
        webhook.Property(item => item.Status).HasMaxLength(30).IsRequired();
        webhook.Property(item => item.Error).HasMaxLength(4000);
        webhook.HasIndex(item => new { item.TenantId, item.ProviderKey, item.ConnectionKey, item.ExternalEventId }).IsUnique();
        webhook.HasIndex(item => new { item.TenantId, item.Status, item.ReceivedAt });
    }

    private static void ConfigureCanonicalModel(ModelBuilder modelBuilder)
    {
        var owner = Table<SalesOwner>(modelBuilder, "sales_owners");
        owner.Property(item => item.DisplayName).HasMaxLength(300).IsRequired();
        owner.Property(item => item.Email).HasMaxLength(320);
        owner.HasIndex(item => new { item.TenantId, item.DisplayName });
        owner.HasIndex(item => new { item.TenantId, item.Email });

        var team = Table<SalesTeam>(modelBuilder, "sales_teams");
        team.Property(item => item.Key).HasMaxLength(100).IsRequired();
        team.Property(item => item.Name).HasMaxLength(200).IsRequired();
        team.HasIndex(item => new { item.TenantId, item.Key }).IsUnique();

        var teamMember = Table<SalesTeamMember>(modelBuilder, "sales_team_members");
        teamMember.HasIndex(item => new { item.TenantId, item.TeamId, item.OwnerId, item.ValidFrom }).IsUnique();
        teamMember.HasOne(item => item.Team).WithMany(item => item.Members).HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Restrict);
        teamMember.HasOne(item => item.Owner).WithMany(item => item.TeamMemberships).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);

        var customer = Table<SalesCustomer>(modelBuilder, "sales_customers");
        customer.Property(item => item.Name).HasMaxLength(300).IsRequired();
        customer.Property(item => item.LegalName).HasMaxLength(300);
        customer.Property(item => item.TaxNumber).HasMaxLength(100);
        customer.Property(item => item.WebsiteDomain).HasMaxLength(300);
        customer.Property(item => item.Industry).HasMaxLength(200);
        customer.Property(item => item.PostalCode).HasMaxLength(30);
        customer.Property(item => item.City).HasMaxLength(200);
        customer.Property(item => item.RegionCode).HasMaxLength(100);
        customer.Property(item => item.CountryCode).HasMaxLength(10);
        customer.Property(item => item.AddressLine1).HasMaxLength(300);
        customer.Property(item => item.HouseNumber).HasMaxLength(50);
        customer.Property(item => item.Status).HasMaxLength(100).IsRequired();
        customer.Property(item => item.GeocodingStatus).HasMaxLength(40);
        customer.Property(item => item.LifetimeRevenue).HasPrecision(18, 2);
        customer.Property(item => item.Latitude).HasPrecision(9, 6);
        customer.Property(item => item.Longitude).HasPrecision(9, 6);
        customer.HasIndex(item => new { item.TenantId, item.Name });
        customer.HasIndex(item => new { item.TenantId, item.TaxNumber });
        customer.HasOne(item => item.Owner).WithMany(item => item.Customers).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var customerRelationship = Table<SalesCustomerRelationship>(modelBuilder, "sales_customer_relationships");
        customerRelationship.Property(item => item.RelationshipType).HasMaxLength(80).IsRequired();
        customerRelationship.Property(item => item.Source).HasMaxLength(100);
        customerRelationship.Property(item => item.Notes).HasMaxLength(2000);
        customerRelationship.HasIndex(item => new { item.TenantId, item.ParentCustomerId, item.ChildCustomerId, item.RelationshipType }).IsUnique();
        customerRelationship.ToTable("sales_customer_relationships", table =>
            table.HasCheckConstraint("CK_sales_customer_relationships_not_self", "\"ParentCustomerId\" <> \"ChildCustomerId\""));
        customerRelationship.HasOne(item => item.ParentCustomer).WithMany(item => item.ParentRelationships).HasForeignKey(item => item.ParentCustomerId).OnDelete(DeleteBehavior.Restrict);
        customerRelationship.HasOne(item => item.ChildCustomer).WithMany(item => item.ChildRelationships).HasForeignKey(item => item.ChildCustomerId).OnDelete(DeleteBehavior.Restrict);

        var customerStatus = Table<SalesCustomerStatusHistory>(modelBuilder, "sales_customer_status_history");
        customerStatus.Property(item => item.Status).HasMaxLength(100).IsRequired();
        customerStatus.HasIndex(item => new { item.TenantId, item.CustomerId, item.ValidFrom });
        customerStatus.HasOne(item => item.Customer).WithMany(item => item.StatusHistory).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Restrict);

        var contact = Table<SalesContact>(modelBuilder, "sales_contacts");
        contact.Property(item => item.Name).HasMaxLength(300).IsRequired();
        contact.Property(item => item.FirstName).HasMaxLength(150);
        contact.Property(item => item.LastName).HasMaxLength(150);
        contact.Property(item => item.Email).HasMaxLength(320);
        contact.Property(item => item.NormalizedEmail).HasMaxLength(320);
        contact.Property(item => item.Phone).HasMaxLength(100);
        contact.Property(item => item.NormalizedPhone).HasMaxLength(100);
        contact.Property(item => item.MobilePhone).HasMaxLength(100);
        contact.Property(item => item.JobTitle).HasMaxLength(200);
        contact.HasIndex(item => new { item.TenantId, item.NormalizedEmail });
        contact.HasIndex(item => new { item.TenantId, item.NormalizedPhone });
        contact.HasOne(item => item.Customer).WithMany(item => item.Contacts).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);

        var lead = Table<SalesLead>(modelBuilder, "sales_leads");
        lead.Property(item => item.Name).HasMaxLength(300).IsRequired();
        lead.Property(item => item.CompanyName).HasMaxLength(300);
        lead.Property(item => item.Email).HasMaxLength(320);
        lead.Property(item => item.NormalizedEmail).HasMaxLength(320);
        lead.Property(item => item.Phone).HasMaxLength(100);
        lead.Property(item => item.NormalizedPhone).HasMaxLength(100);
        lead.Property(item => item.Status).HasMaxLength(100).IsRequired();
        lead.Property(item => item.Source).HasMaxLength(150);
        lead.HasIndex(item => new { item.TenantId, item.NormalizedEmail });
        lead.HasIndex(item => new { item.TenantId, item.NormalizedPhone });
        lead.HasIndex(item => new { item.TenantId, item.ResponseDueAt });
        lead.HasOne(item => item.Customer).WithMany(item => item.Leads).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);
        lead.HasOne(item => item.Contact).WithMany().HasForeignKey(item => item.ContactId).OnDelete(DeleteBehavior.SetNull);
        lead.HasOne(item => item.Owner).WithMany(item => item.Leads).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var category = Table<SalesProductCategory>(modelBuilder, "sales_product_categories");
        category.Property(item => item.Key).HasMaxLength(100).IsRequired();
        category.Property(item => item.Name).HasMaxLength(200).IsRequired();
        category.HasIndex(item => new { item.TenantId, item.Key }).IsUnique();

        var product = Table<SalesProduct>(modelBuilder, "sales_products");
        product.Property(item => item.Key).HasMaxLength(100).IsRequired();
        product.Property(item => item.Name).HasMaxLength(300).IsRequired();
        product.Property(item => item.Description).HasMaxLength(2000);
        product.HasIndex(item => new { item.TenantId, item.Key }).IsUnique();
        product.HasIndex(item => new { item.TenantId, item.Name });
        product.HasOne(item => item.Category).WithMany(item => item.Products).HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.SetNull);

        var pipeline = Table<SalesPipeline>(modelBuilder, "sales_pipelines");
        pipeline.Property(item => item.Key).HasMaxLength(100).IsRequired();
        pipeline.Property(item => item.Name).HasMaxLength(200).IsRequired();
        pipeline.Property(item => item.Description).HasMaxLength(2000);
        pipeline.HasIndex(item => new { item.TenantId, item.Key }).IsUnique();

        var stage = Table<SalesPipelineStage>(modelBuilder, "sales_pipeline_stages");
        stage.Property(item => item.Key).HasMaxLength(100).IsRequired();
        stage.Property(item => item.Name).HasMaxLength(200).IsRequired();
        stage.Property(item => item.StageType).HasMaxLength(30).IsRequired();
        stage.Property(item => item.Probability).HasPrecision(5, 4);
        stage.HasIndex(item => new { item.TenantId, item.PipelineId, item.Key }).IsUnique();
        stage.HasOne(item => item.Pipeline).WithMany(item => item.Stages).HasForeignKey(item => item.PipelineId).OnDelete(DeleteBehavior.Restrict);

        var deal = Table<SalesDeal>(modelBuilder, "sales_deals");
        deal.Property(item => item.Name).HasMaxLength(300).IsRequired();
        deal.Property(item => item.Amount).HasPrecision(18, 2);
        deal.Property(item => item.Currency).HasMaxLength(10);
        deal.Property(item => item.Status).HasMaxLength(100).IsRequired();
        deal.Property(item => item.LossReason).HasMaxLength(300);
        deal.Property(item => item.DurationMonths).HasPrecision(10, 2);
        deal.HasIndex(item => new { item.TenantId, item.CustomerId });
        deal.HasIndex(item => new { item.TenantId, item.OwnerId, item.Status });
        deal.HasIndex(item => new { item.TenantId, item.PipelineId, item.PipelineStageId });
        deal.HasIndex(item => new { item.TenantId, item.ClosingAt });
        deal.HasIndex(item => new { item.TenantId, item.ContractEndAt });
        deal.HasIndex(item => new { item.TenantId, item.LastActivityAt });
        deal.HasOne(item => item.Customer).WithMany(item => item.Deals).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);
        deal.HasOne(item => item.Owner).WithMany(item => item.Deals).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);
        deal.HasOne(item => item.Pipeline).WithMany(item => item.Deals).HasForeignKey(item => item.PipelineId).OnDelete(DeleteBehavior.SetNull);
        deal.HasOne(item => item.PipelineStage).WithMany(item => item.Deals).HasForeignKey(item => item.PipelineStageId).OnDelete(DeleteBehavior.SetNull);
        deal.HasOne(item => item.Product).WithMany(item => item.Deals).HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.SetNull);

        var contract = Table<SalesContract>(modelBuilder, "sales_contracts");
        contract.Property(item => item.ContractNumber).HasMaxLength(150);
        contract.Property(item => item.Status).HasMaxLength(50).IsRequired();
        contract.Property(item => item.DurationMonths).HasPrecision(10, 2);
        contract.Property(item => item.RecurringAmount).HasPrecision(18, 2);
        contract.Property(item => item.Currency).HasMaxLength(10);
        contract.HasIndex(item => new { item.TenantId, item.CustomerId, item.Status });
        contract.HasIndex(item => new { item.TenantId, item.EndAt });
        contract.HasOne(item => item.Customer).WithMany(item => item.Contracts).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Restrict);
        contract.HasOne(item => item.Deal).WithMany(item => item.Contracts).HasForeignKey(item => item.DealId).OnDelete(DeleteBehavior.SetNull);
        contract.HasOne(item => item.Product).WithMany(item => item.Contracts).HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.SetNull);
        contract.HasOne(item => item.Owner).WithMany(item => item.Contracts).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var stageHistory = Table<SalesDealStageHistory>(modelBuilder, "sales_deal_stage_history");
        stageHistory.Property(item => item.StageKeySnapshot).HasMaxLength(150).IsRequired();
        stageHistory.Property(item => item.SourceEventKey).HasMaxLength(200);
        stageHistory.HasIndex(item => new { item.TenantId, item.DealId, item.EnteredAt });
        stageHistory.HasOne(item => item.Deal).WithMany(item => item.StageHistory).HasForeignKey(item => item.DealId).OnDelete(DeleteBehavior.Restrict);
        stageHistory.HasOne(item => item.Pipeline).WithMany(item => item.StageHistory).HasForeignKey(item => item.PipelineId).OnDelete(DeleteBehavior.SetNull);
        stageHistory.HasOne(item => item.PipelineStage).WithMany(item => item.StageHistory).HasForeignKey(item => item.PipelineStageId).OnDelete(DeleteBehavior.SetNull);

        var activity = Table<SalesActivity>(modelBuilder, "sales_activities");
        activity.Property(item => item.ActivityType).HasMaxLength(100).IsRequired();
        activity.Property(item => item.Subject).HasMaxLength(500);
        activity.Property(item => item.Direction).HasMaxLength(50);
        activity.Property(item => item.ConnectionStatus).HasMaxLength(50);
        activity.Property(item => item.ConversationClass).HasMaxLength(50);
        activity.Property(item => item.Result).HasMaxLength(200);
        activity.Property(item => item.CorrectionNote).HasMaxLength(1000);
        activity.HasIndex(item => new { item.TenantId, item.OccurredAt });
        activity.HasIndex(item => new { item.TenantId, item.OwnerId, item.OccurredAt });
        activity.HasOne(item => item.Owner).WithMany(item => item.Activities).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var activityRelation = Table<SalesActivityRelation>(modelBuilder, "sales_activity_relations");
        activityRelation.Property(item => item.TargetType).HasMaxLength(50).IsRequired();
        activityRelation.Property(item => item.RelationRole).HasMaxLength(50);
        activityRelation.HasIndex(item => new { item.TenantId, item.ActivityId, item.TargetType, item.TargetId }).IsUnique();
        activityRelation.HasIndex(item => new { item.TenantId, item.TargetType, item.TargetId });
        activityRelation.HasOne(item => item.Activity).WithMany(item => item.Relations).HasForeignKey(item => item.ActivityId).OnDelete(DeleteBehavior.Cascade);

        var appointment = Table<SalesAppointment>(modelBuilder, "sales_appointments");
        appointment.Property(item => item.Subject).HasMaxLength(500);
        appointment.Property(item => item.Status).HasMaxLength(100).IsRequired();
        appointment.Property(item => item.AppointmentType).HasMaxLength(150);
        appointment.HasIndex(item => new { item.TenantId, item.StartsAt });
        appointment.HasIndex(item => new { item.TenantId, item.OwnerId, item.StartsAt });
        appointment.HasOne(item => item.Owner).WithMany(item => item.Appointments).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var appointmentRelation = Table<SalesAppointmentRelation>(modelBuilder, "sales_appointment_relations");
        appointmentRelation.Property(item => item.TargetType).HasMaxLength(50).IsRequired();
        appointmentRelation.Property(item => item.RelationRole).HasMaxLength(50);
        appointmentRelation.HasIndex(item => new { item.TenantId, item.AppointmentId, item.TargetType, item.TargetId }).IsUnique();
        appointmentRelation.HasIndex(item => new { item.TenantId, item.TargetType, item.TargetId });
        appointmentRelation.HasOne(item => item.Appointment).WithMany(item => item.Relations).HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Cascade);

        var appointmentStatus = Table<SalesAppointmentStatusHistory>(modelBuilder, "sales_appointment_status_history");
        appointmentStatus.Property(item => item.Status).HasMaxLength(100).IsRequired();
        appointmentStatus.Property(item => item.Source).HasMaxLength(100);
        appointmentStatus.Property(item => item.Notes).HasMaxLength(1000);
        appointmentStatus.HasIndex(item => new { item.TenantId, item.AppointmentId, item.ChangedAt });
        appointmentStatus.HasOne(item => item.Appointment).WithMany(item => item.StatusHistory).HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWorkflowModel(ModelBuilder modelBuilder)
    {
        var workItem = Table<SalesWorkItem>(modelBuilder, "sales_work_items");
        workItem.Property(item => item.WorkItemType).HasMaxLength(60).IsRequired();
        workItem.Property(item => item.Status).HasMaxLength(40).IsRequired();
        workItem.Property(item => item.Title).HasMaxLength(500).IsRequired();
        workItem.Property(item => item.Reason).HasColumnType("text");
        workItem.Property(item => item.PriorityScore).HasPrecision(10, 2);
        workItem.Property(item => item.SourceRuleCode).HasMaxLength(50);
        workItem.Property(item => item.CompletedBy).HasMaxLength(256);
        workItem.HasIndex(item => new { item.TenantId, item.Status, item.DueAt });
        workItem.HasIndex(item => new { item.TenantId, item.OwnerId, item.Status });
        workItem.HasIndex(item => new { item.TenantId, item.PriorityScore });
        workItem.HasOne(item => item.Owner).WithMany(item => item.WorkItems).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);
        workItem.HasOne(item => item.SourceRuleRun).WithMany(item => item.WorkItems).HasForeignKey(item => item.SourceRuleRunId).OnDelete(DeleteBehavior.SetNull);

        var workRelation = Table<SalesWorkItemRelation>(modelBuilder, "sales_work_item_relations");
        workRelation.Property(item => item.TargetType).HasMaxLength(50).IsRequired();
        workRelation.Property(item => item.RelationRole).HasMaxLength(50);
        workRelation.HasIndex(item => new { item.TenantId, item.WorkItemId, item.TargetType, item.TargetId }).IsUnique();
        workRelation.HasIndex(item => new { item.TenantId, item.TargetType, item.TargetId });
        workRelation.HasOne(item => item.WorkItem).WithMany(item => item.Relations).HasForeignKey(item => item.WorkItemId).OnDelete(DeleteBehavior.Cascade);

        var workEvent = Table<SalesWorkItemEvent>(modelBuilder, "sales_work_item_events");
        workEvent.Property(item => item.EventType).HasMaxLength(60).IsRequired();
        workEvent.Property(item => item.DetailsJson).HasColumnType("jsonb");
        workEvent.Property(item => item.ActorSubject).HasMaxLength(256);
        workEvent.HasIndex(item => new { item.TenantId, item.WorkItemId, item.OccurredAt });
        workEvent.HasOne(item => item.WorkItem).WithMany(item => item.Events).HasForeignKey(item => item.WorkItemId).OnDelete(DeleteBehavior.Cascade);

        var rule = Table<SalesRuleDefinition>(modelBuilder, "sales_rule_definitions");
        rule.Property(item => item.Code).HasMaxLength(50).IsRequired();
        rule.Property(item => item.Name).HasMaxLength(200).IsRequired();
        rule.Property(item => item.Description).HasMaxLength(2000);
        rule.Property(item => item.AutomationMode).HasMaxLength(50).IsRequired();
        rule.Property(item => item.ParametersJson).HasColumnType("jsonb");
        rule.Property(item => item.UpdatedBy).HasMaxLength(256);
        rule.HasIndex(item => new { item.TenantId, item.Code, item.Version }).IsUnique();

        var ruleRun = Table<SalesRuleRun>(modelBuilder, "sales_rule_runs");
        ruleRun.Property(item => item.TriggerType).HasMaxLength(50).IsRequired();
        ruleRun.Property(item => item.Status).HasMaxLength(30).IsRequired();
        ruleRun.Property(item => item.Error).HasMaxLength(4000);
        ruleRun.HasIndex(item => new { item.TenantId, item.StartedAt });

        var evaluation = Table<SalesRuleEvaluation>(modelBuilder, "sales_rule_evaluations");
        evaluation.Property(item => item.TargetType).HasMaxLength(50).IsRequired();
        evaluation.Property(item => item.Outcome).HasMaxLength(50).IsRequired();
        evaluation.Property(item => item.ExplanationJson).HasColumnType("jsonb");
        evaluation.HasIndex(item => new { item.TenantId, item.RuleRunId, item.TargetType, item.TargetId }).IsUnique();
        evaluation.HasOne(item => item.RuleRun).WithMany(item => item.Evaluations).HasForeignKey(item => item.RuleRunId).OnDelete(DeleteBehavior.Cascade);
        evaluation.HasOne(item => item.RuleDefinition).WithMany(item => item.Evaluations).HasForeignKey(item => item.RuleDefinitionId).OnDelete(DeleteBehavior.Restrict);
        evaluation.HasOne(item => item.WorkItem).WithMany(item => item.RuleEvaluations).HasForeignKey(item => item.WorkItemId).OnDelete(DeleteBehavior.SetNull);

        var priorityProfile = Table<SalesPriorityProfile>(modelBuilder, "sales_priority_profiles");
        priorityProfile.Property(item => item.Key).HasMaxLength(100).IsRequired();
        priorityProfile.Property(item => item.Name).HasMaxLength(200).IsRequired();
        priorityProfile.Property(item => item.Description).HasMaxLength(2000);
        priorityProfile.Property(item => item.BaseScore).HasPrecision(10, 2);
        priorityProfile.Property(item => item.AgeBonusPerDay).HasPrecision(10, 2);
        priorityProfile.Property(item => item.ValueBonusFactor).HasPrecision(10, 4);
        priorityProfile.Property(item => item.MaximumScore).HasPrecision(10, 2);
        priorityProfile.Property(item => item.UpdatedBy).HasMaxLength(256);
        priorityProfile.HasIndex(item => new { item.TenantId, item.Key }).IsUnique();

        var priorityWeight = Table<SalesPriorityWeight>(modelBuilder, "sales_priority_weights");
        priorityWeight.Property(item => item.WorkItemType).HasMaxLength(60).IsRequired();
        priorityWeight.Property(item => item.Weight).HasPrecision(10, 4);
        priorityWeight.Property(item => item.ConfigurationJson).HasColumnType("jsonb");
        priorityWeight.HasIndex(item => new { item.TenantId, item.PriorityProfileId, item.WorkItemType }).IsUnique();
        priorityWeight.HasOne(item => item.PriorityProfile).WithMany(item => item.Weights).HasForeignKey(item => item.PriorityProfileId).OnDelete(DeleteBehavior.Cascade);

        var fiscalYear = Table<SalesFiscalYear>(modelBuilder, "sales_fiscal_years");
        fiscalYear.Property(item => item.Name).HasMaxLength(100).IsRequired();
        fiscalYear.Property(item => item.TimeZone).HasMaxLength(100).IsRequired();
        fiscalYear.Property(item => item.StartsAt).HasColumnType("date");
        fiscalYear.Property(item => item.EndsAt).HasColumnType("date");
        fiscalYear.HasIndex(item => new { item.TenantId, item.Name }).IsUnique();

        var targetPeriod = Table<SalesTargetPeriod>(modelBuilder, "sales_target_periods");
        targetPeriod.Property(item => item.PeriodType).HasMaxLength(30).IsRequired();
        targetPeriod.Property(item => item.DistributionWeight).HasPrecision(7, 4);
        targetPeriod.Property(item => item.StartsAt).HasColumnType("date");
        targetPeriod.Property(item => item.EndsAt).HasColumnType("date");
        targetPeriod.HasIndex(item => new { item.TenantId, item.FiscalYearId, item.PeriodType, item.PeriodNumber }).IsUnique();
        targetPeriod.HasOne(item => item.FiscalYear).WithMany(item => item.TargetPeriods).HasForeignKey(item => item.FiscalYearId).OnDelete(DeleteBehavior.Cascade);

        var target = Table<SalesTarget>(modelBuilder, "sales_targets");
        target.Property(item => item.TargetType).HasMaxLength(60).IsRequired();
        target.Property(item => item.AppointmentType).HasMaxLength(150);
        target.Property(item => item.TargetValue).HasPrecision(18, 2);
        target.Property(item => item.Currency).HasMaxLength(10);
        target.Property(item => item.ApprovedBy).HasMaxLength(256);
        target.Property(item => item.ValidFrom).HasColumnType("date");
        target.Property(item => item.ValidTo).HasColumnType("date");
        target.HasIndex(item => new { item.TenantId, item.OwnerId, item.FiscalYearId, item.TargetType, item.TargetPeriodId }).IsUnique();
        target.HasOne(item => item.FiscalYear).WithMany(item => item.Targets).HasForeignKey(item => item.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
        target.HasOne(item => item.TargetPeriod).WithMany(item => item.Targets).HasForeignKey(item => item.TargetPeriodId).OnDelete(DeleteBehavior.SetNull);
        target.HasOne(item => item.Owner).WithMany(item => item.Targets).HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);

        var calendar = Table<SalesWorkCalendar>(modelBuilder, "sales_work_calendars");
        calendar.Property(item => item.Key).HasMaxLength(100).IsRequired();
        calendar.Property(item => item.Name).HasMaxLength(200).IsRequired();
        calendar.Property(item => item.TimeZone).HasMaxLength(100).IsRequired();
        calendar.HasIndex(item => new { item.TenantId, item.Key }).IsUnique();

        var workingHours = Table<SalesWorkingHours>(modelBuilder, "sales_working_hours");
        workingHours.Property(item => item.StartAt).HasColumnType("time");
        workingHours.Property(item => item.EndAt).HasColumnType("time");
        workingHours.Property(item => item.BreakStartAt).HasColumnType("time");
        workingHours.Property(item => item.BreakEndAt).HasColumnType("time");
        workingHours.HasIndex(item => new { item.TenantId, item.CalendarId, item.DayOfWeek }).IsUnique();
        workingHours.HasOne(item => item.Calendar).WithMany(item => item.WorkingHours).HasForeignKey(item => item.CalendarId).OnDelete(DeleteBehavior.Cascade);

        var holiday = Table<SalesHoliday>(modelBuilder, "sales_holidays");
        holiday.Property(item => item.Name).HasMaxLength(200).IsRequired();
        holiday.Property(item => item.Date).HasColumnType("date");
        holiday.HasIndex(item => new { item.TenantId, item.CalendarId, item.Date }).IsUnique();
        holiday.HasOne(item => item.Calendar).WithMany(item => item.Holidays).HasForeignKey(item => item.CalendarId).OnDelete(DeleteBehavior.Cascade);

        var template = Table<SalesCommunicationTemplate>(modelBuilder, "sales_communication_templates");
        template.Property(item => item.Key).HasMaxLength(100).IsRequired();
        template.Property(item => item.Name).HasMaxLength(200).IsRequired();
        template.Property(item => item.Channel).HasMaxLength(50).IsRequired();
        template.Property(item => item.SubjectTemplate).HasMaxLength(1000);
        template.Property(item => item.BodyTemplate).HasColumnType("text").IsRequired();
        template.Property(item => item.UpdatedBy).HasMaxLength(256);
        template.HasIndex(item => new { item.TenantId, item.Key, item.Version }).IsUnique();

        var notification = Table<SalesNotification>(modelBuilder, "sales_notifications");
        notification.Property(item => item.RecipientSubject).HasMaxLength(256).IsRequired();
        notification.Property(item => item.Title).HasMaxLength(500);
        notification.Property(item => item.PayloadJson).HasColumnType("jsonb");
        notification.Property(item => item.DeliveryStatus).HasMaxLength(30).IsRequired();
        notification.HasIndex(item => new { item.TenantId, item.RecipientSubject, item.DeliveryStatus, item.DueAt });
        notification.HasOne(item => item.WorkItem).WithMany(item => item.Notifications).HasForeignKey(item => item.WorkItemId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureAnalyticsModel(ModelBuilder modelBuilder)
    {
        var snapshotRun = Table<SalesSnapshotRun>(modelBuilder, "sales_snapshot_runs");
        snapshotRun.Property(item => item.SnapshotType).HasMaxLength(50).IsRequired();
        snapshotRun.Property(item => item.Status).HasMaxLength(30).IsRequired();
        snapshotRun.Property(item => item.Error).HasMaxLength(4000);
        snapshotRun.Property(item => item.SnapshotDate).HasColumnType("date");
        snapshotRun.HasIndex(item => new { item.TenantId, item.SnapshotDate, item.SnapshotType }).IsUnique();

        var pipelineSnapshot = Table<SalesPipelineSnapshot>(modelBuilder, "sales_pipeline_snapshots");
        pipelineSnapshot.Property(item => item.OpenAmount).HasPrecision(18, 2);
        pipelineSnapshot.Property(item => item.WeightedAmount).HasPrecision(18, 2);
        pipelineSnapshot.Property(item => item.Currency).HasMaxLength(10);
        pipelineSnapshot.Property(item => item.SnapshotDate).HasColumnType("date");
        pipelineSnapshot.HasIndex(item => new { item.TenantId, item.SnapshotDate, item.PipelineId, item.PipelineStageId, item.OwnerId }).IsUnique();
        pipelineSnapshot.HasOne(item => item.SnapshotRun).WithMany(item => item.PipelineSnapshots).HasForeignKey(item => item.SnapshotRunId).OnDelete(DeleteBehavior.Cascade);
        pipelineSnapshot.HasOne(item => item.Pipeline).WithMany().HasForeignKey(item => item.PipelineId).OnDelete(DeleteBehavior.Restrict);
        pipelineSnapshot.HasOne(item => item.PipelineStage).WithMany().HasForeignKey(item => item.PipelineStageId).OnDelete(DeleteBehavior.Restrict);
        pipelineSnapshot.HasOne(item => item.Owner).WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var kpi = Table<SalesKpiSnapshot>(modelBuilder, "sales_kpi_snapshots");
        kpi.Property(item => item.PeriodType).HasMaxLength(20).IsRequired();
        kpi.Property(item => item.MetricKey).HasMaxLength(100).IsRequired();
        kpi.Property(item => item.Industry).HasMaxLength(200);
        kpi.Property(item => item.CountryCode).HasMaxLength(10);
        kpi.Property(item => item.PostalRegion).HasMaxLength(10);
        kpi.Property(item => item.Value).HasPrecision(20, 4);
        kpi.Property(item => item.Numerator).HasPrecision(20, 4);
        kpi.Property(item => item.Denominator).HasPrecision(20, 4);
        kpi.Property(item => item.Currency).HasMaxLength(10);
        kpi.Property(item => item.DetailsJson).HasColumnType("jsonb");
        kpi.Property(item => item.SnapshotDate).HasColumnType("date");
        kpi.Property(item => item.PeriodStart).HasColumnType("date");
        kpi.Property(item => item.PeriodEnd).HasColumnType("date");
        kpi.HasIndex(item => new { item.TenantId, item.SnapshotDate, item.MetricKey, item.PeriodType, item.OwnerId, item.PipelineId, item.ProductCategoryId, item.Industry, item.CountryCode, item.PostalRegion }).IsUnique();
        kpi.HasOne(item => item.SnapshotRun).WithMany(item => item.KpiSnapshots).HasForeignKey(item => item.SnapshotRunId).OnDelete(DeleteBehavior.Cascade);
        kpi.HasOne(item => item.Owner).WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);
        kpi.HasOne(item => item.Pipeline).WithMany().HasForeignKey(item => item.PipelineId).OnDelete(DeleteBehavior.SetNull);
        kpi.HasOne(item => item.ProductCategory).WithMany().HasForeignKey(item => item.ProductCategoryId).OnDelete(DeleteBehavior.SetNull);

        var activitySnapshot = Table<SalesActivitySnapshot>(modelBuilder, "sales_activity_snapshots");
        activitySnapshot.Property(item => item.ActivityType).HasMaxLength(100);
        activitySnapshot.Property(item => item.SnapshotDate).HasColumnType("date");
        activitySnapshot.Property(item => item.PeriodStart).HasColumnType("date");
        activitySnapshot.Property(item => item.PeriodEnd).HasColumnType("date");
        activitySnapshot.HasIndex(item => new { item.TenantId, item.SnapshotDate, item.PeriodStart, item.PeriodEnd, item.OwnerId, item.ActivityType }).IsUnique();
        activitySnapshot.HasOne(item => item.SnapshotRun).WithMany(item => item.ActivitySnapshots).HasForeignKey(item => item.SnapshotRunId).OnDelete(DeleteBehavior.Cascade);
        activitySnapshot.HasOne(item => item.Owner).WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.SetNull);

        var customerStatusSnapshot = Table<SalesCustomerStatusSnapshot>(modelBuilder, "sales_customer_status_snapshots");
        customerStatusSnapshot.Property(item => item.Status).HasMaxLength(100).IsRequired();
        customerStatusSnapshot.Property(item => item.LifetimeRevenue).HasPrecision(18, 2);
        customerStatusSnapshot.Property(item => item.SnapshotDate).HasColumnType("date");
        customerStatusSnapshot.Property(item => item.PeriodStart).HasColumnType("date");
        customerStatusSnapshot.Property(item => item.PeriodEnd).HasColumnType("date");
        customerStatusSnapshot.HasIndex(item => new { item.TenantId, item.SnapshotDate, item.PeriodStart, item.PeriodEnd, item.Status }).IsUnique();
        customerStatusSnapshot.HasOne(item => item.SnapshotRun).WithMany(item => item.CustomerStatusSnapshots).HasForeignKey(item => item.SnapshotRunId).OnDelete(DeleteBehavior.Cascade);

        var finding = Table<SalesDataQualityFinding>(modelBuilder, "sales_data_quality_findings");
        finding.Property(item => item.Code).HasMaxLength(100).IsRequired();
        finding.Property(item => item.Severity).HasMaxLength(30).IsRequired();
        finding.Property(item => item.Status).HasMaxLength(30).IsRequired();
        finding.Property(item => item.EntityType).HasMaxLength(100).IsRequired();
        finding.Property(item => item.FieldName).HasMaxLength(200);
        finding.Property(item => item.Message).HasMaxLength(2000).IsRequired();
        finding.Property(item => item.DetailsJson).HasColumnType("jsonb");
        finding.Property(item => item.Fingerprint).HasMaxLength(256).IsRequired();
        finding.Property(item => item.ResolvedBy).HasMaxLength(256);
        finding.HasIndex(item => new { item.TenantId, item.Fingerprint }).IsUnique();
        finding.HasIndex(item => new { item.TenantId, item.Status, item.Severity });

        var duplicate = Table<SalesDuplicateCandidate>(modelBuilder, "sales_duplicate_candidates");
        duplicate.Property(item => item.Score).HasPrecision(10, 4);
        duplicate.Property(item => item.Confidence).HasMaxLength(30).IsRequired();
        duplicate.Property(item => item.MatchDetailsJson).HasColumnType("jsonb");
        duplicate.Property(item => item.Status).HasMaxLength(30).IsRequired();
        duplicate.HasIndex(item => new { item.TenantId, item.CustomerAId, item.CustomerBId }).IsUnique();
        duplicate.HasIndex(item => new { item.TenantId, item.Status, item.Score });
        duplicate.HasOne(item => item.CustomerA).WithMany().HasForeignKey(item => item.CustomerAId).OnDelete(DeleteBehavior.Restrict);
        duplicate.HasOne(item => item.CustomerB).WithMany().HasForeignKey(item => item.CustomerBId).OnDelete(DeleteBehavior.Restrict);

        var duplicateDecision = Table<SalesDuplicateDecision>(modelBuilder, "sales_duplicate_decisions");
        duplicateDecision.Property(item => item.Decision).HasMaxLength(50).IsRequired();
        duplicateDecision.Property(item => item.DecidedBy).HasMaxLength(256).IsRequired();
        duplicateDecision.Property(item => item.FieldSelectionsJson).HasColumnType("jsonb");
        duplicateDecision.Property(item => item.Notes).HasMaxLength(2000);
        duplicateDecision.HasIndex(item => new { item.TenantId, item.DuplicateCandidateId, item.DecidedAt });
        duplicateDecision.HasOne(item => item.DuplicateCandidate).WithMany(item => item.Decisions).HasForeignKey(item => item.DuplicateCandidateId).OnDelete(DeleteBehavior.Cascade);
        duplicateDecision.HasOne(item => item.LeadingCustomer).WithMany().HasForeignKey(item => item.LeadingCustomerId).OnDelete(DeleteBehavior.SetNull);

        var merge = Table<SalesMergeOperation>(modelBuilder, "sales_merge_operations");
        merge.Property(item => item.Status).HasMaxLength(30).IsRequired();
        merge.Property(item => item.ApprovedBy).HasMaxLength(256);
        merge.Property(item => item.WritebackReference).HasMaxLength(300);
        merge.Property(item => item.Error).HasMaxLength(4000);
        merge.HasIndex(item => new { item.TenantId, item.Status, item.StartedAt });
        merge.HasOne(item => item.DuplicateCandidate).WithMany(item => item.MergeOperations).HasForeignKey(item => item.DuplicateCandidateId).OnDelete(DeleteBehavior.SetNull);
        merge.HasOne(item => item.SourceCustomer).WithMany().HasForeignKey(item => item.SourceCustomerId).OnDelete(DeleteBehavior.Restrict);
        merge.HasOne(item => item.TargetCustomer).WithMany().HasForeignKey(item => item.TargetCustomerId).OnDelete(DeleteBehavior.Restrict);

        var ownerChange = Table<SalesOwnerChangeRequest>(modelBuilder, "sales_owner_change_requests");
        ownerChange.Property(item => item.TargetType).HasMaxLength(50).IsRequired();
        ownerChange.Property(item => item.SourceRuleCode).HasMaxLength(50);
        ownerChange.Property(item => item.Reason).HasMaxLength(2000).IsRequired();
        ownerChange.Property(item => item.Status).HasMaxLength(30).IsRequired();
        ownerChange.Property(item => item.DecidedBy).HasMaxLength(256);
        ownerChange.Property(item => item.WritebackStatus).HasMaxLength(30);
        ownerChange.HasIndex(item => new { item.TenantId, item.Status, item.RequestedAt });
        ownerChange.HasIndex(item => new { item.TenantId, item.TargetType, item.TargetId });
        ownerChange.HasOne(item => item.Customer).WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);
        ownerChange.HasOne(item => item.OldOwner).WithMany().HasForeignKey(item => item.OldOwnerId).OnDelete(DeleteBehavior.SetNull);
        ownerChange.HasOne(item => item.ProposedOwner).WithMany().HasForeignKey(item => item.ProposedOwnerId).OnDelete(DeleteBehavior.SetNull);

        var audit = Table<SalesAuditLog>(modelBuilder, "sales_audit_log");
        audit.Property(item => item.ActorSubject).HasMaxLength(256);
        audit.Property(item => item.ActorDisplayName).HasMaxLength(300);
        audit.Property(item => item.Action).HasMaxLength(100).IsRequired();
        audit.Property(item => item.EntityType).HasMaxLength(100).IsRequired();
        audit.Property(item => item.BeforeJson).HasColumnType("jsonb");
        audit.Property(item => item.AfterJson).HasColumnType("jsonb");
        audit.Property(item => item.CorrelationId).HasMaxLength(200);
        audit.HasIndex(item => new { item.TenantId, item.EntityType, item.EntityId, item.OccurredAt });
        audit.HasIndex(item => new { item.TenantId, item.OccurredAt });
    }

    private static EntityTypeBuilder<TEntity> Table<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : SalesEntity
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(tableName);
        entity.HasKey(item => item.Id);
        return entity;
    }
}
