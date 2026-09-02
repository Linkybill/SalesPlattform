using System.Security.Claims;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;
using IdentityPlatform.Shared.Authorization;
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
using SalesPlattform.Backend.Services.Mail;

var builder = WebApplication.CreateBuilder(args);
builder.AddIdentityPlatform();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization(authorization =>
{
        authorization.AddPolicy("sales-access", policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
            TenantApplicationRole.IsInRole(context.User, "sales-user")
            || TenantApplicationRole.IsInRole(context.User, "sales-manager")
            || TenantApplicationRole.IsInRole(context.User, "sales-management")
            || TenantApplicationRole.IsInRole(context.User, "sales-backoffice")));
        authorization.AddPolicy("sales-layout-access", policy => policy
            .RequireAuthenticatedUser());
});

builder.Services.AddPlatformTenantDatabase<SalesPlattformDbContext>(
    "20260902170000_AddCrmServiceAndCommercialRecords");
builder.Services.AddHttpClient<PlatformJobLivenessClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddApplicationSettings<SalesPlattformDbContext>(builder.Configuration, options =>
{
    options.ApplicationKey = builder.Configuration["IdentityPlatform:ApplicationKey"] ?? "sales-plattform";
});
builder.Services.AddScoped<TenantAdminAccessService>();
builder.Services.AddScoped<HelloWorldService>();
builder.Services.AddScoped<OwnerMappingService>();
builder.Services.AddScoped<SalesApplicationSettingsService>();
builder.Services.AddScoped<WorklistService>();
builder.Services.AddScoped<SalesMailSettingsService>();
builder.Services.AddScoped<SalesMailDeliveryProviderRegistry>();
builder.Services.AddScoped<ISalesMailDeliveryProvider, SmtpSalesMailDeliveryProvider>();
builder.Services.AddScoped<SalesNotificationOutboxService>();
builder.Services.AddScoped<SalesNotificationDeliveryService>();
builder.Services.AddScoped<CrmTaskMirrorService>();
builder.Services.AddScoped<SalesDashboardLayoutService>();
builder.Services.AddScoped<SalesReportService>();
builder.Services.AddScoped<SalesSnapshotService>();
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

app.MapGet("/api/reports/dashboard", async (
    ClaimsPrincipal user,
    SalesReportService reports,
    string? timeframe,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await reports.GetDashboardAsync(user, timeframe, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
}).RequireAuthorization("sales-access");

app.MapGet("/api/reports/layout", async (
    ClaimsPrincipal user,
    SalesReportService reports,
    TenantAdminAccessService tenantAdminAccess,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (!SalesDashboardLayoutService.HasAnyRole(
                user,
                "sales-user",
                "sales-manager",
                "sales-management",
                "sales-backoffice"
            )
            && !await tenantAdminAccess.IsCurrentTenantAdminAsync(user, cancellationToken))
        {
            return Results.Forbid();
        }

        return Results.Ok(await reports.GetLayoutAsync(user, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
}).RequireAuthorization("sales-layout-access");

app.MapPut("/api/reports/layout", async (
    SaveSalesDashboardLayoutRequest request,
    ClaimsPrincipal user,
    SalesReportService reports,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await reports.SaveLayoutAsync(request, user, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}).RequireAuthorization("sales-layout-access");

app.MapGet("/api/owner-mappings", async (
    ClaimsPrincipal user,
    OwnerMappingService ownerMappings,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await ownerMappings.GetAsync(user, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}).RequireAuthorization("sales-access");

app.MapPut("/api/owner-mappings", async (
    SaveOwnerMappingRequest request,
    ClaimsPrincipal user,
    OwnerMappingService ownerMappings,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await ownerMappings.SaveAsync(request, user, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}).RequireAuthorization("sales-access");

app.MapDelete("/api/owner-mappings/{platformUserEmail}", async (
    string platformUserEmail,
    ClaimsPrincipal user,
    OwnerMappingService ownerMappings,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await ownerMappings.DeleteAsync(platformUserEmail, user, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}).RequireAuthorization("sales-access");

app.MapGet("/api/worklist", async (
    ClaimsPrincipal user,
    WorklistService worklist,
    bool? refresh,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out _))
    {
        return Results.BadRequest(new { message = "The access token does not contain a valid tenant_id claim." });
    }

    var result = await worklist.GetAsync(user, refresh ?? false, cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("sales-access");

app.MapPost("/api/worklist/{workItemId:guid}/snooze", async (
    Guid workItemId,
    SnoozeWorklistItemRequest request,
    ClaimsPrincipal user,
    WorklistService worklist,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out _))
        return Results.BadRequest(new { message = "The access token does not contain a valid tenant_id claim." });

    try
    {
        var result = await worklist.SnoozeAsync(workItemId, request, user, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}).RequireAuthorization("sales-access");

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
