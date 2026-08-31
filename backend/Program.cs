using System.Security.Claims;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Services;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Hosting;
using IdentityPlatform.Shared.Registration;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Zoho;

var builder = WebApplication.CreateBuilder(args);
builder.AddIdentityPlatform();

builder.Services.AddPlatformTenantDatabase<SalesPlattformDbContext>();
builder.Services.AddApplicationSettings<SalesPlattformDbContext>(builder.Configuration, options =>
{
    options.ApplicationKey = builder.Configuration["IdentityPlatform:ApplicationKey"] ?? "sales-plattform";
});
builder.Services.AddScoped<HelloWorldService>();
builder.Services.AddOptions<ZohoOptions>()
    .Bind(builder.Configuration.GetSection("Zoho"));
builder.Services.AddScoped<ZohoConfigurationService>();
builder.Services.AddScoped<ZohoConnectionStore>();
builder.Services.AddScoped<ZohoTokenService>();
builder.Services.AddScoped<ZohoOAuthService>();
builder.Services.AddScoped<ZohoCrmAdapter>();
builder.Services.AddScoped<ICrmAdapter>(services =>
    services.GetRequiredService<ZohoCrmAdapter>());
builder.Services.AddScoped<ZohoSyncService>();

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
