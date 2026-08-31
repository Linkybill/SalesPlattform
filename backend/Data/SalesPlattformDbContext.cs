using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.EntityFrameworkCore;

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

    public DbSet<IntegrationSyncCursor> IntegrationSyncCursors => Set<IntegrationSyncCursor>();

    public DbSet<SalesCustomer> SalesCustomers => Set<SalesCustomer>();

    public DbSet<SalesContact> SalesContacts => Set<SalesContact>();

    public DbSet<SalesLead> SalesLeads => Set<SalesLead>();

    public DbSet<SalesProduct> SalesProducts => Set<SalesProduct>();

    public DbSet<SalesPipeline> SalesPipelines => Set<SalesPipeline>();

    public DbSet<SalesPipelineStage> SalesPipelineStages => Set<SalesPipelineStage>();

    public DbSet<SalesDeal> SalesDeals => Set<SalesDeal>();

    public DbSet<SalesDealStageHistory> SalesDealStageHistory => Set<SalesDealStageHistory>();

    public DbSet<SalesActivity> SalesActivities => Set<SalesActivity>();

    public DbSet<SalesAppointment> SalesAppointments => Set<SalesAppointment>();

    protected override void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HelloWorldRecord>(entity =>
        {
            entity.ToTable("hello_world_records");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Message).HasMaxLength(500).IsRequired();
            entity.Property(record => record.CreatedAt).IsRequired();
            entity.HasIndex(record => record.CreatedAt);
        });

        modelBuilder.Entity<IntegrationConnection>(entity =>
        {
            entity.ToTable("integration_connections");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
            entity.Property(item => item.ConnectionKey).HasMaxLength(100).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ExternalOrganizationId).HasMaxLength(200);
            entity.Property(item => item.ApiDomain).HasMaxLength(300).IsRequired();
            entity.HasIndex(item => new { item.ProviderKey, item.ConnectionKey }).IsUnique();
        });

        modelBuilder.Entity<IntegrationOAuthState>(entity =>
        {
            entity.ToTable("integration_oauth_states");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
            entity.Property(item => item.StateHash).HasMaxLength(128).IsRequired();
            entity.Property(item => item.UserSubject).HasMaxLength(256).IsRequired();
            entity.HasIndex(item => item.StateHash).IsUnique();
            entity.HasIndex(item => item.ExpiresAt);
        });

        modelBuilder.Entity<IntegrationEntityLink>(entity =>
        {
            entity.ToTable("integration_entity_links");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.ExternalId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.InternalEntityType).HasMaxLength(80).IsRequired();
            entity.HasIndex(item => new { item.ProviderKey, item.EntityType, item.ExternalId }).IsUnique();
            entity.HasIndex(item => new { item.InternalEntityType, item.InternalEntityId });
        });

        modelBuilder.Entity<IntegrationRawRecord>(entity =>
        {
            entity.ToTable("integration_raw_records");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.ExternalId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(item => new { item.ProviderKey, item.EntityType, item.ExternalId }).IsUnique();
            entity.HasIndex(item => item.SyncedAt);
        });

        modelBuilder.Entity<IntegrationSyncRun>(entity =>
        {
            entity.ToTable("integration_sync_runs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
            entity.Property(item => item.Mode).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Error).HasMaxLength(4000);
            entity.HasIndex(item => item.StartedAt);
        });

        modelBuilder.Entity<IntegrationSyncCursor>(entity =>
        {
            entity.ToTable("integration_sync_cursors");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ProviderKey).HasMaxLength(50).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.LastExternalId).HasMaxLength(200);
            entity.HasIndex(item => new { item.ProviderKey, item.EntityType }).IsUnique();
        });

        modelBuilder.Entity<SalesCustomer>(entity =>
        {
            entity.ToTable("sales_customers");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Industry).HasMaxLength(200);
            entity.Property(item => item.PostalCode).HasMaxLength(30);
            entity.Property(item => item.City).HasMaxLength(200);
            entity.Property(item => item.Country).HasMaxLength(100);
            entity.Property(item => item.OwnerExternalId).HasMaxLength(200);
            entity.Property(item => item.Status).HasMaxLength(100);
            entity.HasIndex(item => item.Name);
        });

        modelBuilder.Entity<SalesContact>(entity =>
        {
            entity.ToTable("sales_contacts");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Email).HasMaxLength(320);
            entity.Property(item => item.Phone).HasMaxLength(100);
            entity.Property(item => item.JobTitle).HasMaxLength(200);
            entity.HasIndex(item => item.Email);
        });

        modelBuilder.Entity<SalesLead>(entity =>
        {
            entity.ToTable("sales_leads");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300).IsRequired();
            entity.Property(item => item.CompanyName).HasMaxLength(300);
            entity.Property(item => item.Email).HasMaxLength(320);
            entity.Property(item => item.Phone).HasMaxLength(100);
            entity.Property(item => item.Status).HasMaxLength(100);
            entity.Property(item => item.Source).HasMaxLength(150);
            entity.Property(item => item.OwnerExternalId).HasMaxLength(200);
            entity.HasIndex(item => item.Email);
            entity.HasIndex(item => item.LastContactAt);
        });

        modelBuilder.Entity<SalesProduct>(entity =>
        {
            entity.ToTable("sales_products");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(200);
            entity.HasIndex(item => item.Name).IsUnique();
        });

        modelBuilder.Entity<SalesPipeline>(entity =>
        {
            entity.ToTable("sales_pipelines");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Key).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(item => item.Key).IsUnique();
        });

        modelBuilder.Entity<SalesPipelineStage>(entity =>
        {
            entity.ToTable("sales_pipeline_stages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Key).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(item => new { item.PipelineId, item.Key }).IsUnique();
        });

        modelBuilder.Entity<SalesDeal>(entity =>
        {
            entity.ToTable("sales_deals");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.Currency).HasMaxLength(10);
            entity.Property(item => item.PipelineKey).HasMaxLength(100);
            entity.Property(item => item.StageKey).HasMaxLength(150);
            entity.Property(item => item.ProductName).HasMaxLength(300);
            entity.Property(item => item.DurationMonths).HasPrecision(10, 2);
            entity.Property(item => item.Status).HasMaxLength(100);
            entity.Property(item => item.LossReason).HasMaxLength(300);
            entity.Property(item => item.OwnerExternalId).HasMaxLength(200);
            entity.HasOne(item => item.Customer)
                .WithMany(item => item.Deals)
                .HasForeignKey(item => item.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(item => item.CustomerId);
            entity.HasIndex(item => item.ClosingAt);
            entity.HasIndex(item => item.StageKey);
        });

        modelBuilder.Entity<SalesDealStageHistory>(entity =>
        {
            entity.ToTable("sales_deal_stage_history");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.StageKey).HasMaxLength(150).IsRequired();
            entity.HasIndex(item => new { item.DealId, item.EnteredAt });
        });

        modelBuilder.Entity<SalesActivity>(entity =>
        {
            entity.ToTable("sales_activities");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ActivityType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Subject).HasMaxLength(500);
            entity.Property(item => item.Direction).HasMaxLength(50);
            entity.Property(item => item.Result).HasMaxLength(200);
            entity.Property(item => item.OwnerExternalId).HasMaxLength(200);
            entity.Property(item => item.RelatedEntityType).HasMaxLength(80);
            entity.Property(item => item.RelatedExternalId).HasMaxLength(200);
            entity.HasIndex(item => item.OccurredAt);
            entity.HasIndex(item => new { item.RelatedEntityType, item.RelatedExternalId });
        });

        modelBuilder.Entity<SalesAppointment>(entity =>
        {
            entity.ToTable("sales_appointments");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Subject).HasMaxLength(500);
            entity.Property(item => item.Status).HasMaxLength(100).IsRequired();
            entity.Property(item => item.AppointmentType).HasMaxLength(150);
            entity.Property(item => item.OwnerExternalId).HasMaxLength(200);
            entity.Property(item => item.RelatedEntityType).HasMaxLength(80);
            entity.Property(item => item.RelatedExternalId).HasMaxLength(200);
            entity.HasIndex(item => item.StartsAt);
        });
    }
}
