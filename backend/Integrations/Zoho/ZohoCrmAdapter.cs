using System.Diagnostics;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed record ZohoModuleMetadata(
    string ApiName,
    bool ApiSupported,
    string? GeneratedType);

public sealed record ZohoNotificationRegistration(
    string ChannelId,
    DateTimeOffset? ExpiresAt);

public sealed class ZohoCrmAdapter(
    IHttpClientFactory httpClientFactory,
    ZohoTokenService tokenService,
    ICrmApiUsageRecorder apiUsage,
    ILogger<ZohoCrmAdapter> logger) : ICrmAdapter
{
    private bool organizationLookupCompleted;
    private string? organizationDomain;
    private string? crmWebBaseUrl;

    public string ProviderKey => CrmProviders.Zoho;

    public async Task<CrmConnectionTestResult> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        // A connection test verifies authentication only. Module and field
        // metadata belongs to the explicit schema-cache job and must not be
        // fetched implicitly here.
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        return new CrmConnectionTestResult(ProviderKey, true, token.ApiDomain, []);
    }

    public async Task<IReadOnlyCollection<string>> GetModulesAsync(
        CancellationToken cancellationToken = default)
        => (await GetModuleMetadataAsync(cancellationToken))
            .Select(module => module.ApiName)
            .ToArray();

    /// <summary>
    /// Reads the module catalog once and keeps the capability flags supplied
    /// by Zoho. The schema cache uses these flags to decide which metadata
    /// requests are legal before it sends any fields/layouts/related-list
    /// request. In particular, modules with api_supported=false must never be
    /// sent to a metadata endpoint.
    /// </summary>
    public async Task<IReadOnlyCollection<ZohoModuleMetadata>> GetModuleMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "/crm/v8/settings/modules",
            cancellationToken);
        using var document = await ParseDocumentAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("modules", out var modules)
            || modules.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = modules.EnumerateArray()
            .Select(module =>
            {
                var apiName = module.TryGetProperty("api_name", out var apiNameValue)
                    ? apiNameValue.GetString()
                    : null;
                var apiSupported = module.TryGetProperty("api_supported", out var apiSupportedValue)
                    && apiSupportedValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? apiSupportedValue.GetBoolean()
                        // Older Zoho responses did not always include the
                        // capability flag. Keep those modules eligible and
                        // let the explicit endpoint capability matrix decide.
                        : true;
                var generatedType = module.TryGetProperty("generated_type", out var generatedTypeValue)
                    && generatedTypeValue.ValueKind == JsonValueKind.String
                        ? generatedTypeValue.GetString()
                        : null;
                return new ZohoModuleMetadata(apiName ?? string.Empty, apiSupported, generatedType);
            })
            .Where(module => !string.IsNullOrWhiteSpace(module.ApiName))
            .Append(new ZohoModuleMetadata("Users", true, "default"))
            .Where(module => module.ApiName.Length > 0)
            .DistinctBy(module => module.ApiName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(module => module.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return result;
    }

    public async Task<IReadOnlyCollection<CrmFieldMetadata>> GetFieldsAsync(
        string module,
        CancellationToken cancellationToken = default)
    {
        if (module.Equals("Activities", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Users", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Pipelines", StringComparison.OrdinalIgnoreCase)
            || module.Equals("PipelineStages", StringComparison.OrdinalIgnoreCase)
            || module.Equals("DealStageHistory", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Analytics", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        using var response = await SendAsync(
            HttpMethod.Get,
            $"/crm/v8/settings/fields?module={Uri.EscapeDataString(module)}",
            cancellationToken);
        using var document = await ParseDocumentAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("fields", out var fields)
            || fields.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = fields.EnumerateArray()
            .Select(field => new CrmFieldMetadata(
                GetString(field, "api_name") ?? string.Empty,
                GetString(field, "field_label") ?? GetString(field, "display_label"),
                GetString(field, "data_type")))
            .Where(field => !string.IsNullOrWhiteSpace(field.ApiName))
            .ToArray();

        return result;
    }

    public async Task<IReadOnlyCollection<JsonElement>> GetLayoutsAsync(
        string module,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/crm/v8/settings/layouts?module={Uri.EscapeDataString(module)}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return [];
        using var document = await ParseDocumentAsync(response, cancellationToken);
        return GetArray(document.RootElement, "layouts")
            .Select(layout => layout.Clone())
            .ToArray();
    }

    public async Task<IReadOnlyCollection<JsonElement>> GetRelatedListsAsync(
        string module,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/crm/v8/settings/related_lists?module={Uri.EscapeDataString(module)}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return [];
        using var document = await ParseDocumentAsync(response, cancellationToken);
        return GetArray(document.RootElement, "related_lists")
            .Concat(GetArray(document.RootElement, "relatedLists"))
            .Select(relatedList => relatedList.Clone())
            .ToArray();
    }

    public async Task<IReadOnlyCollection<JsonElement>> GetPipelinesAsync(
        string module,
        IReadOnlyCollection<JsonElement> layouts,
        CancellationToken cancellationToken = default)
    {
        if (!module.Equals("Deals", StringComparison.OrdinalIgnoreCase))
            return [];

        var layoutIds = layouts
            .Select(layout => GetString(layout, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var pipelines = new List<JsonElement>();
        foreach (var layoutId in layoutIds)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"/crm/v8/settings/pipeline?layout_id={Uri.EscapeDataString(layoutId)}",
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                continue;

            using var document = await ParseDocumentAsync(response, cancellationToken);
            if (!document.RootElement.TryGetProperty("pipeline", out var pipeline)
                || pipeline.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Zoho liefert für das Deals-Layout '{layoutId}' keine 'pipeline'-Liste. "
                    + "Die Antwort ist leer oder hat ein unerwartetes Format.");
            }

            pipelines.AddRange(pipeline.EnumerateArray().Select(item => item.Clone()));
        }

        var result = DistinctPipelines(pipelines);
        return result.Length > 0
            ? result
            : await GetPipelinePayloadsFromDealsAsync(cancellationToken);
    }

    public IReadOnlyCollection<CrmExternalRecord> GetPipelineRecordsFromCache(
        ZohoSchemaCacheSnapshot schema)
        => schema.GetPipelines()
            .Select(pipeline => new CrmExternalRecord(
                ProviderKey,
                "Pipelines",
                GetString(pipeline, "id")
                    ?? GetString(pipeline, "actual_value", "display_value")
                    ?? Guid.NewGuid().ToString("N"),
                pipeline.Clone(),
                null))
            .ToArray();

    public IReadOnlyCollection<CrmExternalRecord> GetPipelineStageRecordsFromCache(
        ZohoSchemaCacheSnapshot schema)
    {
        var result = new List<CrmExternalRecord>();
        foreach (var pipeline in schema.GetPipelines())
        {
            var pipelineId = GetString(pipeline, "id")
                ?? GetString(pipeline, "actual_value", "display_value")
                ?? "default";
            var pipelineName = GetString(pipeline, "display_value", "actual_value") ?? pipelineId;
            if (!pipeline.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var map in maps.EnumerateArray())
            {
                var stageId = GetString(map, "id")
                    ?? GetString(map, "actual_value")
                    ?? GetString(map, "display_value");
                if (string.IsNullOrWhiteSpace(stageId)) continue;
                var node = JsonNode.Parse(map.GetRawText())?.AsObject() ?? new JsonObject();
                node["pipeline_id"] = pipelineId;
                node["pipeline_name"] = pipelineName;
                var payload = node.AsObject().Deserialize<JsonElement>();
                result.Add(new CrmExternalRecord(
                    ProviderKey,
                    "PipelineStages",
                    $"{pipelineId}:{stageId}",
                    payload,
                    null));
            }
        }

        return result;
    }

    public async Task<IReadOnlyCollection<CrmExternalRecord>> GetRecordsAsync(
        string module,
        IReadOnlyCollection<string> fields,
        DateTimeOffset? modifiedSince = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureOrganizationContextAsync(cancellationToken);
        if (module.Equals("Users", StringComparison.OrdinalIgnoreCase))
            return await GetUsersAsync(modifiedSince, cancellationToken);
        if (module.Equals("Pipelines", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Zoho-Pipelines müssen aus dem lokalen Schema-Cache gelesen werden.");
        if (module.Equals("PipelineStages", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Zoho-PipelineStages müssen aus dem lokalen Schema-Cache gelesen werden.");
        if (fields.Count == 0)
            throw new ArgumentException("Mindestens ein Zoho-Feld ist erforderlich.", nameof(fields));

        return await GetPagedRecordsAsync(
            module,
            fields,
            $"/crm/v8/{Uri.EscapeDataString(module)}",
            cancellationToken,
            modifiedSince: modifiedSince);
    }

    public async Task<CrmExternalRecord?> GetRecordAsync(
        string module,
        string externalId,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Das Zoho-Modul ist erforderlich.", nameof(module));
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("Die Zoho-Remote-ID ist erforderlich.", nameof(externalId));

        await EnsureOrganizationContextAsync(cancellationToken);
        var rawExternalId = externalId.Contains(':', StringComparison.Ordinal)
            ? externalId[(externalId.LastIndexOf(':') + 1)..]
            : externalId;
        var path = $"/crm/v8/{Uri.EscapeDataString(module)}/{Uri.EscapeDataString(rawExternalId)}";
        if (fields.Count > 0)
            path += $"?fields={Uri.EscapeDataString(string.Join(',', fields.Take(50)))}";

        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        using var document = await ParseDocumentAsync(response, cancellationToken);
        return ParseRecords(document.RootElement, module, null).FirstOrDefault();
    }

    public async Task<ZohoNotificationRegistration> RegisterNotificationsAsync(
        string notifyUrl,
        string token,
        string channelId,
        string module,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notifyUrl))
            throw new ArgumentException("Für Zoho-Notifications ist eine erreichbare Notify-URL erforderlich.", nameof(notifyUrl));
        if (string.IsNullOrWhiteSpace(token) || token.Length > 50)
            throw new ArgumentException("Der Zoho-Verification-Token muss zwischen 1 und 50 Zeichen lang sein.", nameof(token));
        if (!long.TryParse(channelId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Die Zoho-Channel-ID muss numerisch sein.", nameof(channelId));

        var watch = new JsonObject
        {
            ["token"] = token,
            ["notify_url"] = notifyUrl,
            ["channel_id"] = channelId,
            ["channel_expiry"] = DateTimeOffset.UtcNow
                .AddDays(6)
                .AddHours(23)
                .ToString("O", CultureInfo.InvariantCulture),
            ["return_affected_field_values"] = true,
            ["notify_on_related_action"] = false
        };
        var events = new JsonArray();
        foreach (var operation in NotificationOperations(module))
            events.Add(operation);
        watch["events"] = events;
        var payload = new JsonObject { ["watch"] = new JsonArray(watch) };
        using var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await SendAsync(
            HttpMethod.Post,
            "/crm/v8/actions/watch",
            cancellationToken,
            content: content,
            recordsAffected: 1);
        using var document = await ParseDocumentAsync(response, cancellationToken);
        foreach (var watchResult in GetArray(document.RootElement, "watch"))
        {
            var details = watchResult.TryGetProperty("details", out var detailsValue)
                ? detailsValue
                : default;
            foreach (var eventDetails in GetArray(details, "events"))
            {
                var returnedChannelId = GetString(eventDetails, "channel_id");
                if (string.IsNullOrWhiteSpace(returnedChannelId))
                    continue;
                var expiryText = GetString(eventDetails, "channel_expiry");
                return new ZohoNotificationRegistration(
                    returnedChannelId,
                    DateTimeOffset.TryParse(
                        expiryText,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var expiresAt)
                        ? expiresAt.ToUniversalTime()
                        : null);
            }
        }

        throw new InvalidOperationException(
            $"Zoho hat für das Modul '{module}' keine Notification-Subscription bestätigt.");
    }

    public async Task DisableNotificationsAsync(
        string channelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return;
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/crm/v8/actions/watch?channel_ids={Uri.EscapeDataString(channelId)}",
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<CrmDeletedRecord>> GetDeletedRecordsAsync(
        string module,
        DateTimeOffset? deletedSince = null,
        CancellationToken cancellationToken = default)
    {
        // Emails and the pipeline/history pseudo-modules are related lists or
        // metadata endpoints and do not have a module-level deleted API.
        if (module.Equals("Users", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Emails", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Pipelines", StringComparison.OrdinalIgnoreCase)
            || module.Equals("PipelineStages", StringComparison.OrdinalIgnoreCase)
            || module.Equals("DealStageHistory", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var records = new List<CrmDeletedRecord>();
        var page = 1;
        const int pageSize = 200;
        while (true)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"/crm/v8/{Uri.EscapeDataString(module)}/deleted?type=all&per_page={pageSize}&page={page}",
                cancellationToken,
                deletedSince);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                break;

            using var document = await ParseDocumentAsync(response, cancellationToken);
            foreach (var item in GetArray(document.RootElement, "data"))
            {
                var id = GetString(item, "id");
                var deletedAt = ZohoFieldReader.DateTimeOffset(item, "deleted_time");
                if (string.IsNullOrWhiteSpace(id) || deletedAt is null)
                    continue;

                records.Add(new CrmDeletedRecord(
                    ProviderKey,
                    module,
                    CanonicalEntityType(module),
                    CanonicalizeExternalId(module, id),
                    deletedAt.Value));
            }

            if (GetNestedBool(document.RootElement, "info", "more_records") != true)
                break;
            page++;
        }

        return records;
    }

    public async Task<CrmTaskWriteResult> CreateTaskAsync(
        CrmTaskWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["data"] = new JsonArray(BuildTaskPayload(request, includeWorkflowDefaults: true))
        };
        using var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await SendAsync(
            HttpMethod.Post,
            "/crm/v8/Tasks",
            cancellationToken,
            content: content,
            recordsAffected: 1);
        using var document = await ParseDocumentAsync(response, cancellationToken);
        var result = GetArray(document.RootElement, "data").FirstOrDefault();
        var externalId = result.ValueKind == JsonValueKind.Object
            ? GetNestedString(result, "details", "id")
            : null;
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new InvalidOperationException(
                "Zoho hat nach dem Anlegen der CRM-Aufgabe keine Remote-ID geliefert.");
        }

        await EnsureOrganizationContextAsync(cancellationToken);
        return new CrmTaskWriteResult(
            ProviderKey,
            "default",
            CanonicalizeExternalId("Tasks", externalId),
            result.Clone(),
            BuildRecordUrl("Tasks", externalId, result));
    }

    public async Task UpdateTaskAsync(
        string externalId,
        CrmTaskWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var rawExternalId = externalId.Contains(':', StringComparison.Ordinal)
            ? externalId[(externalId.LastIndexOf(':') + 1)..]
            : externalId;
        if (string.IsNullOrWhiteSpace(rawExternalId))
            throw new ArgumentException("Für die CRM-Aufgabe fehlt die Remote-ID.", nameof(externalId));

        var payload = new JsonObject
        {
            // Status and priority are deliberately omitted on updates. A user
            // may have completed or reprioritized the CRM task since creation.
            ["data"] = new JsonArray(BuildTaskPayload(request, includeWorkflowDefaults: false))
        };
        using var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await SendAsync(
            HttpMethod.Put,
            $"/crm/v8/Tasks/{Uri.EscapeDataString(rawExternalId)}",
            cancellationToken,
            content: content,
            recordsAffected: 1);
    }

    private static IReadOnlyCollection<string> NotificationOperations(string module)
        => module.Equals("Users", StringComparison.OrdinalIgnoreCase)
            ? [$"{module}.all"]
            : [$"{module}.create", $"{module}.edit", $"{module}.delete"];

    private static JsonObject BuildTaskPayload(
        CrmTaskWriteRequest request,
        bool includeWorkflowDefaults)
    {
        var task = new JsonObject
        {
            ["Subject"] = request.Subject
        };
        if (includeWorkflowDefaults)
        {
            task["Status"] = "Not Started";
            task["Priority"] = "High";
        }
        if (request.DueAt.HasValue)
            task["Due_Date"] = request.DueAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(request.Description))
            task["Description"] = request.Description;
        if (!string.IsNullOrWhiteSpace(request.OwnerExternalId))
            task["Owner"] = new JsonObject { ["id"] = NormalizeZohoRecordId(request.OwnerExternalId) };

        if (!string.IsNullOrWhiteSpace(request.TargetExternalId))
        {
            var targetType = request.TargetEntityType?.Trim().ToLowerInvariant();
            var targetModule = targetType switch
            {
                "lead" => "Leads",
                "customer" => "Accounts",
                "deal" => "Deals",
                "service-case" => "Cases",
                "offer" => "Quotes",
                "order" => "Sales_Orders",
                "invoice" => "Invoices",
                _ => null
            };
            if (targetModule is not null)
            {
                var isParticipant = targetModule is "Leads";
                task[isParticipant ? "Who_Id" : "What_Id"] =
                    new JsonObject { ["id"] = NormalizeZohoRecordId(request.TargetExternalId) };
                // Zoho requires the concrete related module for every Task
                // relationship. This is also mandatory for Who_Id (Leads),
                // not only for What_Id relationships.
                task["$se_module"] = targetModule;
            }
        }

        return task;
    }

    private static string NormalizeZohoRecordId(string externalId)
    {
        var value = externalId.Trim();
        var separator = value.LastIndexOf(':');
        return separator >= 0 && separator < value.Length - 1
            ? value[(separator + 1)..]
            : value;
    }

    public async Task<IReadOnlyCollection<CrmExternalRecord>> GetRelatedRecordsAsync(
        string parentModule,
        string parentExternalId,
        string relatedList,
        IReadOnlyCollection<string> fields,
        DateTimeOffset? modifiedSince = null,
        CancellationToken cancellationToken = default,
        string? resolvedRelatedListApiName = null)
    {
        await EnsureOrganizationContextAsync(cancellationToken);
        if (fields.Count == 0)
            throw new ArgumentException("Mindestens ein Zoho-Feld ist erforderlich.", nameof(fields));

        if (relatedList.Equals("Emails", StringComparison.OrdinalIgnoreCase))
            return await GetRelatedEmailsAsync(parentModule, parentExternalId, modifiedSince, cancellationToken);

        var relatedListApiName = relatedList.Equals("Stage_History", StringComparison.OrdinalIgnoreCase)
            ? resolvedRelatedListApiName
                ?? throw new InvalidOperationException(
                    "Die Zoho-Stage-History muss über den lokalen Schema-Cache aufgelöst werden.")
            : relatedList;
        var records = await GetPagedRecordsAsync(
            relatedList,
            fields,
            $"/crm/v8/{Uri.EscapeDataString(parentModule)}/{Uri.EscapeDataString(parentExternalId)}/{Uri.EscapeDataString(relatedListApiName)}",
            cancellationToken,
            new CrmRecordRelation(
                ParentEntityType(parentModule),
                parentExternalId,
                "related_to"),
            modifiedSince);
        return records;
    }

    private async Task<IReadOnlyCollection<CrmExternalRecord>> GetRelatedEmailsAsync(
        string parentModule,
        string parentExternalId,
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken)
    {
        var records = new List<CrmExternalRecord>();
        string? index = null;
        var parentRelation = new CrmRecordRelation(
            ParentEntityType(parentModule),
            parentExternalId,
            "related_to");
        do
        {
            var path = $"/crm/v8/{Uri.EscapeDataString(parentModule)}/{Uri.EscapeDataString(parentExternalId)}/Emails";
            if (index is not null) path += $"?index={Uri.EscapeDataString(index)}";
            using var response = await SendAsync(HttpMethod.Get, path, cancellationToken, modifiedSince);
            using var document = await ParseDocumentAsync(response, cancellationToken);
            if (!document.RootElement.TryGetProperty("Emails", out var emails)
                || emails.ValueKind != JsonValueKind.Array)
                break;
            foreach (var email in emails.EnumerateArray())
            {
                var id = GetString(email, "id", "message_id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                records.Add(new CrmExternalRecord(
                    ProviderKey,
                    "Emails",
                    $"Emails:{id}",
                    email.Clone(),
                    ZohoFieldReader.DateTimeOffset(email, "time", "modified_time"),
                    [parentRelation],
                    ExternalUrl: BuildRecordUrl("Emails", id, email)));
            }
            index = GetNestedString(document.RootElement, "info", "next_index");
            if (GetNestedBool(document.RootElement, "info", "more_records") != true)
                index = null;
        } while (index is not null);

        return records;
    }

    private async Task<IReadOnlyCollection<CrmExternalRecord>> GetUsersAsync(
        DateTimeOffset? modifiedSince,
        CancellationToken cancellationToken)
    {
        var records = new List<CrmExternalRecord>();
        var page = 1;
        const int pageSize = 200;
        while (true)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"/crm/v8/users?type=AllUsers&page={page}&per_page={pageSize}",
                cancellationToken,
                modifiedSince);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) break;
            using var document = await ParseDocumentAsync(response, cancellationToken);
            var pageRecords = ParseRecords(document.RootElement, "Users", null, "users").ToArray();
            records.AddRange(pageRecords);
            if (!HasMoreRecords(document.RootElement, pageRecords.Length, pageSize)) break;
            page++;
        }

        return records;
    }

    private static JsonElement[] DistinctPipelines(IEnumerable<JsonElement> pipelines)
        => pipelines
            .GroupBy(
                item => GetString(item, "id", "actual_value", "display_value") ?? item.GetRawText(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Clone())
            .ToArray();

    private async Task<IReadOnlyCollection<JsonElement>> GetPipelinePayloadsFromDealsAsync(
        CancellationToken cancellationToken)
    {
        // Some Zoho organizations expose the pipeline settings endpoint but
        // return 204 for it. The actual pipeline/stage values are still
        // present on the Deals records, so derive a provider-neutral fallback
        // from the same fields that are used by the deal mapper.
        var deals = await GetPagedRecordsAsync(
            "Deals",
            ["id", "Pipeline", "Stage"],
            "/crm/v8/Deals",
            cancellationToken);
        var pipelines = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var stagesByPipeline = new Dictionary<string, Dictionary<string, JsonObject>>(StringComparer.OrdinalIgnoreCase);

        foreach (var deal in deals)
        {
            var stage = ReadNamedValue(deal.Payload, "Stage");
            if (stage is null) continue;
            // Zoho omits the Pipeline field for organizations that only use
            // the default pipeline. The Kanban stage is still present on the
            // deal and must be assigned to that implicit pipeline.
            var pipeline = ReadNamedValue(deal.Payload, "Pipeline")
                ?? ("default", "Default");

            if (!pipelines.TryGetValue(pipeline.Key, out var pipelineNode))
            {
                pipelineNode = new JsonObject
                {
                    ["id"] = pipeline.Key,
                    ["actual_value"] = pipeline.Name,
                    ["display_value"] = pipeline.Name,
                    ["pipeline_name"] = pipeline.Name,
                    ["maps"] = new JsonArray()
                };
                pipelines[pipeline.Key] = pipelineNode;
                stagesByPipeline[pipeline.Key] = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            }

            var stageMap = stagesByPipeline[pipeline.Key];
            if (!stageMap.ContainsKey(stage.Value.Key))
            {
                stageMap[stage.Value.Key] = new JsonObject
                {
                    ["id"] = stage.Value.Key,
                    ["actual_value"] = stage.Value.Name,
                    ["display_value"] = stage.Value.Name,
                    ["pick_list_value"] = stage.Value.Name
                };
            }
        }

        foreach (var pair in pipelines)
        {
            var maps = (JsonArray)pair.Value["maps"]!;
            foreach (var stage in stagesByPipeline[pair.Key].Values)
                maps.Add(stage);
        }

        return pipelines.Values
            .Select(item => JsonSerializer.Deserialize<JsonElement>(item.ToJsonString()))
            .ToArray();
    }

    private static (string Key, string Name)? ReadNamedValue(
        JsonElement record,
        params string[] fieldNames)
    {
        if (record.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var fieldName in fieldNames)
        {
            if (!record.TryGetProperty(fieldName, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Object)
            {
                var key = GetString(value, "id", "actual_value", "value", "name", "display_value");
                var name = GetString(value, "name", "display_value", "actual_value", "value", "id");
                if (!string.IsNullOrWhiteSpace(key))
                    return (key, name ?? key);
            }
            else
            {
                var text = GetString(record, fieldName);
                if (!string.IsNullOrWhiteSpace(text))
                    return (text, text);
            }
        }

        return null;
    }

    private async Task<IReadOnlyCollection<CrmExternalRecord>> GetPagedRecordsAsync(
        string module,
        IReadOnlyCollection<string> fields,
        string path,
        CancellationToken cancellationToken,
        CrmRecordRelation? relation = null,
        DateTimeOffset? modifiedSince = null)
    {
        var records = new List<CrmExternalRecord>();
        var page = 1;
        string? pageToken = null;
        const int pageSize = 200;
        while (true)
        {
            var query = $"fields={Uri.EscapeDataString(string.Join(',', fields.Take(50)))}&per_page={pageSize}";
            query += pageToken is null
                ? $"&page={page}"
                : $"&page_token={Uri.EscapeDataString(pageToken)}";
            using var response = await SendAsync(
                HttpMethod.Get,
                $"{path}?{query}",
                cancellationToken,
                modifiedSince);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) break;
            using var document = await ParseDocumentAsync(response, cancellationToken);
            var pageRecords = ParseRecords(document.RootElement, module, relation).ToArray();
            records.AddRange(pageRecords);
            if (!HasMoreRecords(document.RootElement, pageRecords.Length, pageSize)) break;
            pageToken = GetNestedString(document.RootElement, "info", "next_page_token");
            if (pageToken is null) page++;
        }

        return records;
    }

    private IEnumerable<CrmExternalRecord> ParseRecords(
        JsonElement root,
        string module,
        CrmRecordRelation? relation,
        string dataPropertyName = "data")
    {
        if (!root.TryGetProperty(dataPropertyName, out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Zoho liefert für das Modul '{module}' keine '{dataPropertyName}'-Liste. "
                + "Die Antwort ist leer oder hat ein unerwartetes Format.");
        }
        foreach (var item in data.EnumerateArray())
        {
            var id = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var actualModule = GetString(item, "$module") ?? module;
            if (module.Equals("Stage_History", StringComparison.OrdinalIgnoreCase))
                actualModule = "DealStageHistory";
            // External IDs must be canonical for both normal and related-list
            // reads. Activities and appointments are partitioned by module so
            // a Tasks/id cannot collide with a Calls/id, and the same value is
            // used by the deleted-record endpoint.
            var externalId = CanonicalizeExternalId(actualModule, id);
            yield return new CrmExternalRecord(
                ProviderKey,
                actualModule,
                externalId,
                item.Clone(),
                ZohoFieldReader.DateTimeOffset(item, "Modified_Time", "ModifiedTime", "modified_time"),
                relation is null ? null : [relation],
                ExternalUrl: BuildRecordUrl(actualModule, id, item));
        }
    }

    private async Task EnsureOrganizationContextAsync(CancellationToken cancellationToken)
    {
        if (organizationLookupCompleted)
            return;

        try
        {
            var token = await tokenService.GetAccessTokenAsync(cancellationToken);
            crmWebBaseUrl = ResolveCrmWebBaseUrl(token.ApiDomain);
            using var response = await SendAsync(HttpMethod.Get, "/crm/v8/org", cancellationToken);
            using var document = await ParseDocumentAsync(response, cancellationToken);
            var organization = GetArray(document.RootElement, "org").FirstOrDefault();
            organizationDomain = organization.ValueKind == JsonValueKind.Object
                ? GetString(organization, "domain_name", "domainName")
                : null;
            if (string.IsNullOrWhiteSpace(organizationDomain))
            {
                logger.LogWarning("Zoho CRM lieferte keine Organisation-Domain; externe Datensatz-Links bleiben leer.");
                crmWebBaseUrl = null;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A missing org.read scope must not make the CRM import fail. The
            // next OAuth connection renewal will grant the scope and populate
            // links on the next full or incremental import.
            organizationDomain = null;
            crmWebBaseUrl = null;
            logger.LogWarning(
                exception,
                "Zoho-Organisation konnte nicht gelesen werden; der CRM-Import läuft ohne externe Datensatz-Links weiter.");
        }
        finally
        {
            organizationLookupCompleted = true;
        }
    }

    private string? BuildRecordUrl(string module, string externalId, JsonElement payload)
    {
        var sourceUrl = GetString(payload, "$url", "url", "record_url", "recordUrl");
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsedSourceUrl)
            && (parsedSourceUrl.Scheme == Uri.UriSchemeHttps || parsedSourceUrl.Scheme == Uri.UriSchemeHttp))
        {
            return parsedSourceUrl.AbsoluteUri;
        }

        if (string.IsNullOrWhiteSpace(crmWebBaseUrl) || string.IsNullOrWhiteSpace(organizationDomain))
            return null;

        var tab = module.ToLowerInvariant() switch
        {
            "accounts" => "Accounts",
            "leads" => "Leads",
            "deals" => "Potentials",
            "products" => "Products",
            "calls" => "Calls",
            "tasks" => "Tasks",
            "events" => "Events",
            "meetings" => "Events",
            "appointments" => "Appointments",
            "emails" => "Emails",
            "users" => "Users",
            "cases" => "Cases",
            "quotes" => "Quotes",
            "sales_orders" or "salesorders" => "Sales_Orders",
            "invoices" => "Invoices",
            _ => null
        };
        if (tab is null)
            return null;

        return $"{crmWebBaseUrl}/crm/{Uri.EscapeDataString(organizationDomain)}/tab/{tab}/{Uri.EscapeDataString(externalId)}";
    }

    private static string? ResolveCrmWebBaseUrl(string apiDomain)
    {
        if (!Uri.TryCreate(apiDomain, UriKind.Absolute, out var apiUri))
            return null;

        var host = apiUri.Host.Replace(
            "www.zohoapis.",
            "crm.zoho.",
            StringComparison.OrdinalIgnoreCase);
        if (string.Equals(host, apiUri.Host, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{apiUri.Scheme}://{host}";
    }

    private static bool HasMoreRecords(JsonElement root, int count, int pageSize)
    {
        if (root.TryGetProperty("info", out var info)
            && info.TryGetProperty("more_records", out var moreRecords)
            && moreRecords.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return moreRecords.GetBoolean();
        return count >= pageSize;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        DateTimeOffset? modifiedSince = null,
        HttpContent? content = null,
        int? recordsAffected = null)
    {
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            method,
            $"{token.ApiDomain.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token.Value);
        request.Content = content;
        if (modifiedSince is not null)
        {
            // Zoho documents this header as an ISO-8601 timestamp with an
            // explicit offset. Avoid HttpClient's RFC-1123 normalization.
            request.Headers.TryAddWithoutValidation(
                "If-Modified-Since",
                modifiedSince.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
        }
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            apiUsage.Record(new CrmApiUsageObservation(
                ProviderKey,
                "default",
                method.Method,
                ZohoCrmApiUsageCostModel.NormalizeEndpoint(path),
                $"{method.Method} {ZohoCrmApiUsageCostModel.NormalizeEndpoint(path)}",
                ZohoCrmApiUsageCostModel.Classify(method.Method, path),
                null,
                false,
                false,
                RecordsAffected: recordsAffected,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds));
            throw;
        }

        var statusCode = (int)response.StatusCode;
        var normalizedEndpoint = ZohoCrmApiUsageCostModel.NormalizeEndpoint(path);
        apiUsage.Record(new CrmApiUsageObservation(
            ProviderKey,
            "default",
            method.Method,
            normalizedEndpoint,
            $"{method.Method} {normalizedEndpoint}",
            ZohoCrmApiUsageCostModel.Classify(method.Method, path),
            statusCode,
            response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent,
            response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || statusCode >= 500,
            ProviderUnitsRemaining: ReadIntegerHeader(response, "X-API-CREDITS-REMAINING"),
            RecordsAffected: recordsAffected,
            DurationMilliseconds: stopwatch.ElapsedMilliseconds));
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            // Incremental requests deliberately send If-Modified-Since. Zoho
            // answers with 304 when a module has no changes. Normalize that to
            // the same empty-result response used by its other read endpoints.
            response.Dispose();
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorStatusCode = (int)response.StatusCode;
            var isRateLimit = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
            response.Dispose();
            if (isRateLimit)
            {
                throw new ZohoApiRateLimitException(
                    $"Zoho CRM antwortete auf {path} mit HTTP {errorStatusCode}: {body}");
            }

            if (errorStatusCode == 400
                && body.Contains("NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ZohoUnsupportedModuleException(
                    $"Zoho CRM unterstützt den angeforderten Modul-Endpunkt nicht: {path} (HTTP {errorStatusCode}): {body}");
            }

            throw new InvalidOperationException(
                $"Zoho CRM antwortete auf {path} mit HTTP {errorStatusCode}: {body}");
        }

        return response;
    }

    private static int? ReadIntegerHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;
        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<JsonDocument> ParseDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return JsonDocument.Parse("{}");

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(payload)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(payload);
    }

    private static IEnumerable<JsonElement> GetArray(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }
        return null;
    }

    private static string? GetNestedString(JsonElement element, string parentName, string propertyName)
        => element.TryGetProperty(parentName, out var parent)
            ? GetString(parent, propertyName)
            : null;

    private static bool? GetNestedBool(JsonElement element, string parentName, string propertyName)
        => element.TryGetProperty(parentName, out var parent)
            && parent.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

    private static string ParentEntityType(string module)
        => module.ToLowerInvariant() switch
        {
            "accounts" => CrmEntityTypes.Customer,
            "leads" => CrmEntityTypes.Lead,
            "deals" => CrmEntityTypes.Deal,
            _ => throw new InvalidOperationException($"Unbekanntes Zoho-Bezugsmodul '{module}'.")
        };

    private static string CanonicalEntityType(string module)
        => module.ToLowerInvariant() switch
        {
            "users" => CrmEntityTypes.Owner,
            "accounts" => CrmEntityTypes.Customer,
            "leads" => CrmEntityTypes.Lead,
            "products" => CrmEntityTypes.Product,
            "deals" => CrmEntityTypes.Deal,
            "calls" or "tasks" => CrmEntityTypes.Activity,
            "events" or "meetings" or "appointments" => CrmEntityTypes.Appointment,
            "cases" => CrmEntityTypes.ServiceCase,
            "quotes" => CrmEntityTypes.Offer,
            "sales_orders" or "salesorders" => CrmEntityTypes.Order,
            "invoices" => CrmEntityTypes.Invoice,
            _ => module.ToLowerInvariant()
        };

    public static string CanonicalizeExternalId(string module, string id)
        => module.Equals("Calls", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Tasks", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Events", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Meetings", StringComparison.OrdinalIgnoreCase)
            || module.Equals("Appointments", StringComparison.OrdinalIgnoreCase)
            ? $"{module}:{id}"
            : id;
}
