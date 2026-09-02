using System.Security.Claims;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Hosting;
using IdentityPlatform.Shared.Jobs;
using IdentityPlatform.Shared.Registration;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations;
using SalesPlattform.Backend.Integrations.Zoho;
using SalesPlattform.Backend.Authorization;
using SalesPlattform.Backend.Integrations.Repositories;
using SalesPlattform.Backend.Integrations.Jobs;

var builder = WebApplication.CreateBuilder(args);
builder.AddIdentityPlatform();
builder.Services.AddHttpContextAccessor();

builder.Services.AddPlatformTenantDatabase<SalesPlattformDbContext>();
builder.Services.AddHttpClient<PlatformJobLivenessClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddApplicationSettings<SalesPlattformDbContext>(builder.Configuration, options =>
{
    options.ApplicationKey = builder.Configuration["IdentityPlatform:ApplicationKey"] ?? "sales-plattform";
});
builder.Services.AddScoped<TenantAdminAccessService>();
builder.Services.AddScoped<HelloWorldService>();
builder.Services.AddOptions<ZohoOptions>()
    .Bind(builder.Configuration.GetSection("Zoho"));
builder.Services.AddScoped<ZohoConfigurationService>();
builder.Services.AddScoped<ZohoLegacySecretMigrationService>();
builder.Services.AddScoped<ZohoConnectionStore>();
builder.Services.AddSingleton<ZohoAccessTokenCache>();
builder.Services.AddScoped<ZohoTokenService>();
builder.Services.AddScoped<ZohoOAuthService>();
builder.Services.AddScoped<ZohoCrmAdapter>();
builder.Services.AddSingleton<ZohoCrmRecordMapper>();
builder.Services.AddSingleton<ISalesCrmRepositoryFactory, SalesCrmRepositoryFactory>();
builder.Services.AddScoped<ZohoSyncService>();
builder.Services.AddScoped<ICrmSynchronizationAdapter>(services =>
    services.GetRequiredService<ZohoSyncService>());
builder.Services.AddScoped<CrmProviderSelectionService>();
builder.Services.AddScoped<CrmSynchronizationAdapterRegistry>();
builder.Services.AddScoped<CrmSynchronizationService>();
builder.Services
    .AddIdentityPlatformJobs(builder.Configuration)
    .AddJob<CrmFullImportJob>(new PlatformJobDefinition(
        Key: "crm-full-import",
        Name: "CRM-Vollimport",
        Description: "Gleicht alle verfügbaren Daten des ausgewählten CRM mit dem neutralen Sales-Datenmodell ab.",
        ScheduleMode: PlatformJobScheduleMode.Configurable,
        DefaultCronExpression: "0 2 * * *",
        DefaultTimeZoneId: "Europe/Berlin",
        AllowManualStart: true,
        ComponentKey: "backend",
        ConcurrencyGroup: "crm-synchronization"))
    .AddJob<CrmIncrementalCrawlJob>(new PlatformJobDefinition(
        Key: "crm-incremental-crawl",
        Name: "CRM-Änderungen synchronisieren",
        Description: "Übernimmt neue, geänderte und gelöschte CRM-Datensätze; standardmäßig alle 15 Minuten.",
        ScheduleMode: PlatformJobScheduleMode.Configurable,
        DefaultCronExpression: "*/15 * * * *",
        DefaultTimeZoneId: "Europe/Berlin",
        AllowManualStart: true,
        ComponentKey: "backend",
        ConcurrencyGroup: "crm-synchronization"));

var app = builder.Build();

app.UseIdentityPlatform();
app.MapIdentityPlatformEndpoints();
app.MapApplicationSettingsEndpoints();
app.MapZohoIntegrationEndpoints();

app.MapGet("/api/hello-world", async (
    ClaimsPrincipal user,
    HelloWorldService helloWorld,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId))
    {
        return Results.BadRequest(new
        {
            message = "The access token does not contain a valid tenant_id claim."
        });
    }

    try
    {
        var result = await helloWorld.GetAsync(cancellationToken);
        return Results.Ok(new
        {
            tenantId,
            message = "Hallo aus der SalesPlattform!",
            database = new
            {
                connected = true,
                storedRecords = result.StoredRecords,
                strategy = result.DatabaseStrategy
            }
        });
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).RequireAuthorization();

app.Run();
