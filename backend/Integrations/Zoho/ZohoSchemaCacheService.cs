using System.Text.Json;
using IdentityPlatform.Shared.Database;
using IdentityPlatform.Shared.Jobs;
using Microsoft.EntityFrameworkCore;
using SalesPlattform.Backend.Data;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoSchemaCacheSnapshot(
    IReadOnlyCollection<string> AvailableModules,
    IReadOnlyDictionary<string, IReadOnlyCollection<CrmFieldMetadata>> FieldsByModule,
    IReadOnlyDictionary<string, IReadOnlyCollection<JsonElement>> LayoutsByModule,
    IReadOnlyCollection<JsonElement> Pipelines,
    IReadOnlyDictionary<string, IReadOnlyCollection<JsonElement>> RelatedListsByModule,
    DateTimeOffset FetchedAt,
    string? ExternalOrganizationId)
{
    public IReadOnlyCollection<CrmFieldMetadata> GetFields(string module)
        => FieldsByModule.TryGetValue(module, out var fields) ? fields : [];

    public IReadOnlyCollection<JsonElement> GetLayouts(string module)
        => LayoutsByModule.TryGetValue(module, out var layouts) ? layouts : [];

    public IReadOnlyCollection<JsonElement> GetPipelines()
        => Pipelines;

    public string ResolveRelatedListApiName(string module, string relatedList)
    {
        if (!relatedList.Equals("Stage_History", StringComparison.OrdinalIgnoreCase))
            return relatedList;

        var history = GetRelatedLists(module).FirstOrDefault(item =>
        {
            var apiName = GetString(item, "api_name") ?? string.Empty;
            var label = GetString(item, "display_label") ?? string.Empty;
            return (apiName + " " + label).Contains("stage", StringComparison.OrdinalIgnoreCase)
                && (apiName + " " + label).Contains("history", StringComparison.OrdinalIgnoreCase);
        });
        return GetString(history, "api_name") ?? "DealHistory";
    }

    public IReadOnlyCollection<JsonElement> GetRelatedLists(string module)
        => RelatedListsByModule.TryGetValue(module, out var relatedLists) ? relatedLists : [];

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }
}

/// <summary>
/// Owns the persisted Zoho metadata snapshot. It is intentionally separate
/// from the regular CRM sync: only the explicitly started schema-cache job
/// talks to Zoho's settings endpoints.
/// </summary>
public sealed class ZohoSchemaCacheService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    ZohoCrmAdapter adapter,
    ZohoConfigurationService configuration,
    ICrmApiUsageRecorder apiUsage,
    ILogger<ZohoSchemaCacheService> logger)
{
    private const string ProviderKey = CrmProviders.Zoho;
    private const string ConnectionKey = "default";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ZohoMetadataModules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Leads",
            "Accounts",
            "Deals",
            "Campaigns",
            "Tasks",
            "Cases",
            "Events",
            "Calls",
            "Solutions",
            "Products",
            "Vendors",
            "Price_Books",
            "PriceBooks",
            "Quotes",
            "Sales_Orders",
            "SalesOrders",
            "Purchase_Orders",
            "PurchaseOrders",
            "Invoices",
            "Appointments",
            "Service",
            "Services"
        };

    public async Task<PlatformJobResult> RefreshAsync(
        PlatformJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var usageScope = apiUsage.BeginScope(
            context.TenantId,
            context.RunId,
            context.RequestedBy ?? "system:platform-job",
            CrmApiUsageOrigins.Job);
        try
        {
            await configuration.ResolveCurrentAsync(cancellationToken);
        await context.Logger.InfoAsync(
            "Zoho-Schema-Cache wird aktualisiert. Der CRM-Sync verwendet währenddessen weiterhin den bisherigen Cache.",
            step: "vorbereitung",
            cancellationToken: cancellationToken);

        var moduleMetadata = (await adapter.GetModuleMetadataAsync(cancellationToken))
            .Where(module => !string.IsNullOrWhiteSpace(module.ApiName))
            .Where(module => !IsRemovedModule(module.ApiName))
            .DistinctBy(module => module.ApiName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(module => module.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var liveModules = moduleMetadata
            .Select(module => module.ApiName)
            .ToArray();
        if (liveModules.Length == 0)
            throw new InvalidOperationException("Zoho lieferte keine Module für den Schema-Cache.");
        var metadataModules = moduleMetadata
            .Where(module => SupportsFieldsMetadata(module))
            .ToArray();
        var fieldModules = metadataModules
            .Select(module => module.ApiName)
            .ToArray();
        var layoutModules = metadataModules
            .Where(SupportsLayoutsMetadata)
            .Select(module => module.ApiName)
            .ToArray();
        var relatedListModules = metadataModules
            .Where(SupportsRelatedListsMetadata)
            .Select(module => module.ApiName)
            .ToArray();
        var unsupportedModules = moduleMetadata
            .Where(module => !SupportsFieldsMetadata(module))
            .Select(module => module.ApiName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadataWarnings = new List<string>();

        await context.Logger.InfoAsync(
            $"Zoho-Schema erkannt: {liveModules.Length} Module, Felddefinitionen werden für {fieldModules.Length} Module geladen.",
            step: "module",
            details: JsonSerializer.SerializeToElement(new
            {
                modules = liveModules,
                fieldModules,
                layoutModules,
                relatedListModules,
                skippedModules = unsupportedModules
            }),
            cancellationToken: cancellationToken);
        var totalSteps = 1L
            + fieldModules.Length
            + layoutModules.Length
            + relatedListModules.Length
            + 2L;
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "module",
                Message: $"Zoho-Module gelesen: {liveModules.Length} gefunden.",
                ProgressPercent: 100m / totalSteps,
                ItemsProcessed: 1,
                ItemsTotal: totalSteps),
            cancellationToken);

        var fieldsByModule = new Dictionary<string, IReadOnlyCollection<CrmFieldMetadata>>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fieldModules.Length; index++)
        {
            var module = fieldModules[index];
            await context.Logger.InfoAsync(
                $"Felddefinitionen für Zoho-Modul '{module}' werden abgerufen.",
                step: "felder",
                cancellationToken: cancellationToken);

            IReadOnlyCollection<CrmFieldMetadata> fields;
            try
            {
                fields = await adapter.GetFieldsAsync(module, cancellationToken);
            }
            catch (ZohoUnsupportedModuleException exception)
            {
                unsupportedModules.Add(module);
                // Zoho exposes some pseudo/system modules through the module
                // catalog although their field metadata endpoint is not
                // supported. This is an expected capability difference, not
                // a failed schema refresh; keep it in the result JSON instead
                // of turning every run yellow.
                await context.Logger.InfoAsync(
                    $"Zoho-Modul '{module}' wird beim Schema-Cache übersprungen, weil dieser Settings-Endpunkt das Modul nicht unterstützt.",
                    step: "felder",
                    details: JsonSerializer.SerializeToElement(new
                    {
                        module,
                        reason = exception.Message
                    }),
                    cancellationToken: cancellationToken);
                continue;
            }
            fieldsByModule[module] = fields;
            var processed = index + 2L;
            await context.Logger.InfoAsync(
                $"Felddefinitionen für '{module}' gespeichert: {fields.Count} Felder.",
                step: "felder",
                details: JsonSerializer.SerializeToElement(new
                {
                    module,
                    fields = fields.Count
                }),
                cancellationToken: cancellationToken);
            await context.Progress.ReportAsync(
                new PlatformJobProgress(
                    Step: "felder",
                    Message: $"{module}: {fields.Count} Felddefinitionen gelesen.",
                    ProgressPercent: processed * 100m / totalSteps,
                    ItemsProcessed: processed,
                ItemsTotal: totalSteps),
                cancellationToken);
        }

        var layoutsByModule = new Dictionary<string, IReadOnlyCollection<JsonElement>>(
            StringComparer.OrdinalIgnoreCase);
        var relatedListsByModule = new Dictionary<string, IReadOnlyCollection<JsonElement>>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < layoutModules.Length; index++)
        {
            var module = layoutModules[index];
            await context.Logger.InfoAsync(
                $"Layouts für Zoho-Modul '{module}' werden abgerufen.",
                step: "layouts",
                cancellationToken: cancellationToken);
            IReadOnlyCollection<JsonElement> layouts;
            try
            {
                layouts = await adapter.GetLayoutsAsync(module, cancellationToken);
            }
            catch (ZohoUnsupportedModuleException exception)
            {
                layouts = [];
                metadataWarnings.Add($"Layouts/{module}");
                await context.Logger.InfoAsync(
                    $"Layouts für Zoho-Modul '{module}' werden übersprungen, weil Zoho diesen Settings-Endpunkt nicht unterstützt.",
                    step: "layouts",
                    details: JsonSerializer.SerializeToElement(new { module, reason = exception.Message }),
                    cancellationToken: cancellationToken);
            }
            layoutsByModule[module] = layouts;
            var processed = fieldModules.Length + index + 2L;
            await context.Logger.InfoAsync(
                $"Layouts für '{module}' gespeichert: {layouts.Count} Layouts.",
                step: "layouts",
                details: JsonSerializer.SerializeToElement(new { module, layouts = layouts.Count }),
                cancellationToken: cancellationToken);
            await context.Progress.ReportAsync(
                new PlatformJobProgress(
                    Step: "layouts",
                    Message: $"{module}: {layouts.Count} Layouts gelesen.",
                    ProgressPercent: processed * 100m / totalSteps,
                    ItemsProcessed: processed,
                    ItemsTotal: totalSteps),
                cancellationToken);
        }

        for (var index = 0; index < relatedListModules.Length; index++)
        {
            var module = relatedListModules[index];
            await context.Logger.InfoAsync(
                $"Related Lists für Zoho-Modul '{module}' werden abgerufen.",
                step: "related-lists",
                cancellationToken: cancellationToken);
            IReadOnlyCollection<JsonElement> relatedLists;
            try
            {
                relatedLists = await adapter.GetRelatedListsAsync(module, cancellationToken);
            }
            catch (ZohoUnsupportedModuleException exception)
            {
                relatedLists = [];
                metadataWarnings.Add($"RelatedLists/{module}");
                await context.Logger.InfoAsync(
                    $"Related Lists für Zoho-Modul '{module}' werden übersprungen, weil Zoho diesen Settings-Endpunkt nicht unterstützt.",
                    step: "related-lists",
                    details: JsonSerializer.SerializeToElement(new { module, reason = exception.Message }),
                    cancellationToken: cancellationToken);
            }
            relatedListsByModule[module] = relatedLists;
            var processed = fieldModules.Length + layoutModules.Length + index + 2L;
            await context.Logger.InfoAsync(
                $"Related Lists für '{module}' gespeichert: {relatedLists.Count} Einträge.",
                step: "related-lists",
                details: JsonSerializer.SerializeToElement(new { module, relatedLists = relatedLists.Count }),
                cancellationToken: cancellationToken);
            await context.Progress.ReportAsync(
                new PlatformJobProgress(
                    Step: "related-lists",
                    Message: $"{module}: {relatedLists.Count} Related Lists gelesen.",
                    ProgressPercent: processed * 100m / totalSteps,
                    ItemsProcessed: processed,
                    ItemsTotal: totalSteps),
                cancellationToken);
        }

        var pipelineLayouts = layoutsByModule.TryGetValue("Deals", out var dealLayouts)
            ? dealLayouts
            : [];
        await context.Logger.InfoAsync(
            "Pipeline-Definitionen für das Zoho-Deals-Modul werden abgerufen.",
            step: "pipelines",
            cancellationToken: cancellationToken);
        var pipelines = await adapter.GetPipelinesAsync("Deals", pipelineLayouts, cancellationToken);
        await context.Logger.InfoAsync(
            $"Pipeline-Definitionen gespeichert: {pipelines.Count} Pipelines.",
            step: "pipelines",
            details: JsonSerializer.SerializeToElement(new { pipelines = pipelines.Count }),
            cancellationToken: cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "pipelines",
                Message: $"{pipelines.Count} Pipeline-Definitionen gelesen.",
                ProgressPercent: 100m * (totalSteps - 1L) / totalSteps,
                ItemsProcessed: totalSteps - 1L,
                ItemsTotal: totalSteps),
            cancellationToken);

        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var cache = await session.Context.ZohoSchemaCaches
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey, cancellationToken);
        if (cache is null)
        {
            cache = new ZohoSchemaCache
            {
                Id = Guid.NewGuid(),
                ProviderKey = ProviderKey,
                ConnectionKey = ConnectionKey,
                AvailableModulesJson = "[]",
                FieldsJson = "{}",
                LayoutsJson = "{}",
                PipelinesJson = "[]",
                RelatedListsJson = "{}"
            };
            session.Context.ZohoSchemaCaches.Add(cache);
        }

        var availableModules = fieldModules
            .Append("Users")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        cache.AvailableModulesJson = JsonSerializer.Serialize(availableModules, JsonOptions);
        cache.FieldsJson = JsonSerializer.Serialize(fieldsByModule, JsonOptions);
        cache.LayoutsJson = JsonSerializer.Serialize(layoutsByModule, JsonOptions);
        cache.PipelinesJson = JsonSerializer.Serialize(pipelines, JsonOptions);
        cache.RelatedListsJson = JsonSerializer.Serialize(relatedListsByModule, JsonOptions);
        cache.FetchedAt = DateTimeOffset.UtcNow;
        await session.Context.SaveChangesAsync(cancellationToken);

        var fieldCounts = fieldsByModule.ToDictionary(
            item => item.Key,
            item => item.Value.Count,
            StringComparer.OrdinalIgnoreCase);
        await context.Logger.InfoAsync(
            $"Zoho-Schema-Cache aktualisiert: {availableModules.Length} unterstützte Module, {fieldsByModule.Values.Sum(fields => fields.Count)} Felddefinitionen, {layoutsByModule.Values.Sum(layouts => layouts.Count)} Layouts, {pipelines.Count} Pipelines und {relatedListsByModule.Values.Sum(relatedLists => relatedLists.Count)} Related Lists lokal gespeichert.",
            step: "abschluss",
            details: JsonSerializer.SerializeToElement(new
            {
                modules = availableModules.Length,
                liveModules = liveModules.Length,
                unsupportedModules,
                metadataWarnings,
                fieldDefinitions = fieldsByModule.Values.Sum(fields => fields.Count),
                layouts = layoutsByModule.Values.Sum(layouts => layouts.Count),
                pipelines = pipelines.Count,
                relatedLists = relatedListsByModule.Values.Sum(relatedLists => relatedLists.Count),
                fieldCounts,
                fetchedAt = cache.FetchedAt
            }),
            cancellationToken: cancellationToken);
        await context.Progress.ReportAsync(
            new PlatformJobProgress(
                Step: "abschluss",
                Message: "Zoho-Schema vollständig lokal gespeichert. Weitere CRM-Syncs verwenden diesen Cache.",
                ProgressPercent: 100m,
                ItemsProcessed: totalSteps,
                ItemsTotal: totalSteps),
            cancellationToken);

        logger.LogInformation(
            "Zoho schema cache refreshed for {ModuleCount} supported modules and {FieldCount} fields. {SkippedCount} unsupported module or metadata endpoint(s) were skipped.",
            availableModules.Length,
            fieldsByModule.Values.Sum(fields => fields.Count),
            unsupportedModules.Count + metadataWarnings.Count);
            var cacheDetails = JsonSerializer.SerializeToElement(new
            {
                provider = ProviderKey,
                modules = availableModules.Length,
                liveModules = liveModules.Length,
                unsupportedModules,
                metadataWarnings,
                fieldDefinitions = fieldsByModule.Values.Sum(fields => fields.Count),
                layouts = layoutsByModule.Values.Sum(layouts => layouts.Count),
                pipelines = pipelines.Count,
                relatedLists = relatedListsByModule.Values.Sum(relatedLists => relatedLists.Count),
                fetchedAt = cache.FetchedAt
            });
            return PlatformJobResult.Success(
                unsupportedModules.Count > 0 || metadataWarnings.Count > 0
                    ? "Zoho-Schema-Cache aktualisiert; nicht unterstützte Zoho-Module oder Metadaten-Endpunkte wurden dokumentiert und übersprungen."
                    : "Zoho-Schema-Cache erfolgreich aktualisiert.",
                cacheDetails);
        }
        finally
        {
            var summary = apiUsage.GetPendingSummary();
            await context.Logger.InfoAsync(
                $"CRM-API-Verbrauch erfasst: {summary.Requests} Requests, {summary.EstimatedUnits} Einheiten.",
                "API-Verbrauch",
                JsonSerializer.SerializeToElement(summary),
                CancellationToken.None);
            try
            {
                await apiUsage.FlushAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "CRM-API-Verbrauch für den Zoho-Schema-Cache konnte nicht gespeichert werden.");
            }
        }
    }

    public async Task<ZohoSchemaCacheSnapshot?> GetCachedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var cache = await session.Context.ZohoSchemaCaches
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProviderKey == ProviderKey
                && item.ConnectionKey == ConnectionKey, cancellationToken);
        if (cache is null)
            return null;

        var modules = JsonSerializer.Deserialize<string[]>(cache.AvailableModulesJson, JsonOptions) ?? [];
        var fields = JsonSerializer.Deserialize<Dictionary<string, CrmFieldMetadata[]>>(
            cache.FieldsJson,
            JsonOptions)
            ?? new Dictionary<string, CrmFieldMetadata[]>(StringComparer.OrdinalIgnoreCase);
        var layouts = DeserializeJsonDictionary(cache.LayoutsJson);
        var pipelines = JsonSerializer.Deserialize<JsonElement[]>(cache.PipelinesJson, JsonOptions) ?? [];
        var relatedLists = DeserializeJsonDictionary(cache.RelatedListsJson);
        return new ZohoSchemaCacheSnapshot(
            modules,
            fields.ToDictionary(
                item => item.Key,
                item => (IReadOnlyCollection<CrmFieldMetadata>)item.Value,
                StringComparer.OrdinalIgnoreCase),
            layouts,
            pipelines,
            relatedLists,
            cache.FetchedAt,
            cache.ExternalOrganizationId);
    }

    public async Task<IReadOnlyCollection<CrmFieldMetadata>> GetCachedFieldsAsync(
        string module,
        CancellationToken cancellationToken = default)
        => (await GetCachedAsync(cancellationToken))?.GetFields(module) ?? [];

    private static bool IsSyntheticModule(string module)
        => module.Equals("Activities", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Pipelines", StringComparison.OrdinalIgnoreCase)
            || module.Equals("PipelineStages", StringComparison.OrdinalIgnoreCase)
            || module.Equals("DealStageHistory", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Emails", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Analytics", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemovedModule(string module)
        => module.Equals("Contact", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Contacts", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsFieldsMetadata(ZohoModuleMetadata module)
        => module.ApiSupported
            && !module.ApiName.Equals("Users", StringComparison.OrdinalIgnoreCase)
            && !IsSyntheticModule(module.ApiName)
            && (ZohoMetadataModules.Contains(module.ApiName)
                || IsGeneratedModule(module, "custom", "linking", "subform"));

    private static bool SupportsLayoutsMetadata(ZohoModuleMetadata module)
        => SupportsFieldsMetadata(module)
            && !IsGeneratedModule(module, "linking", "subform");

    private static bool SupportsRelatedListsMetadata(ZohoModuleMetadata module)
        => SupportsLayoutsMetadata(module);

    private static bool IsGeneratedModule(
        ZohoModuleMetadata module,
        params string[] generatedTypes)
        => module.GeneratedType is not null
            && generatedTypes.Contains(module.GeneratedType, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, IReadOnlyCollection<JsonElement>> DeserializeJsonDictionary(
        string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(json, JsonOptions)
            ?? new Dictionary<string, JsonElement[]>(StringComparer.OrdinalIgnoreCase);
        return values.ToDictionary(
            item => item.Key,
            item => (IReadOnlyCollection<JsonElement>)item.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
