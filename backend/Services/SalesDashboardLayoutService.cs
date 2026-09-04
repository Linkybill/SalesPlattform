using System.Security.Claims;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Authorization;

namespace SalesPlattform.Backend.Services;

public static class SalesDashboardNodeTypes
{
    public const string Grid = "grid";
    public const string Accordion = "accordion";
    public const string Tabs = "tabs";
    public const string Heading = "heading";
    public const string Text = "text";
    public const string Report = "report";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Grid, Accordion, Tabs, Heading, Text, Report
    };
}

public static class SalesWebPartCatalog
{
    // The JSON is an implementation detail. Tenant admins edit the tree in the
    // visual page editor, not as a raw application setting.
    public const string SettingKey = "sales.dashboard.layout";
    public const string LegacySettingKey = "sales.dashboard.webparts";

    public static readonly IReadOnlyList<SalesWebPartDefinition> Definitions =
    [
        new("cockpit", "Cockpit", "Statusampel, Kern-KPIs, Funnel und Handlungspunkte.", "sales-manager"),
        new("team", "Team-Steuerung", "Zielerreichung und Aktivität je Mitarbeiter.", "sales-manager"),
        new("meetings", "Meeting Report", "Geplante, durchgeführte, abgesagte und verschobene Termine.", "sales-manager"),
        new("analysis", "Analyse", "Umsatz, Verlustgründe, Verweildauer und Cross-Selling.", "sales-manager"),
        new("customers", "Kundenstamm und Karte", "Kundenverteilung, Gebiete und CRM-Absprünge.", "sales-manager"),
        new("goals", "Ziele und Pace", "Zielerreichung, Zeitanteil und Pace je Mitarbeiter.", "sales-user"),
        new("cleanup", "Aufräumen", "Datenqualität und mögliche Dubletten zur manuellen Prüfung.", "sales-cleanup"),
        new("service", "Servicefälle", "Beschwerden, Supportfälle, Prioritäten und Überfälligkeiten.", "sales-user"),
        new("commercial", "Angebote, Aufträge und Rechnungen", "Kommerzielle Kette von Angebot bis Zahlung.", "sales-user")
    ];

    public static IReadOnlyCollection<SalesDashboardLayoutNode> DefaultLayout =>
    [
        new("intro-heading", SalesDashboardNodeTypes.Heading, "Vertriebsübersicht", Columns: 12),
        new("intro-text", SalesDashboardNodeTypes.Text, Text: "Alle verfügbaren Reports werden aus der Tenant-Datenbank gelesen. Der Tenant-Administrator kann diese Seite direkt bearbeiten und die Reports mit Grids, Tabs, Akkordeons, Überschriften und Texten anordnen.", Columns: 12),
        new(
            "main-grid",
            SalesDashboardNodeTypes.Grid,
            Title: "Reports",
            Columns: 12,
            GridColumns: 12,
            Children: Definitions.Select(definition => new SalesDashboardLayoutNode(
                $"report-{definition.Key}",
                SalesDashboardNodeTypes.Report,
                Title: definition.Title,
                ReportKey: definition.Key,
                Columns: SalesWebPartCatalog.DefaultReportSpan(definition.Key),
                Visible: true)).ToArray())
    ];

    public static int DefaultReportSpan(string key)
        => key switch
        {
            "cockpit" or "team" or "analysis" or "customers" or "goals" or "cleanup" or "commercial" => 12,
            "meetings" or "service" => 6,
            _ => 12
        };

    public static bool Exists(string key)
        => Definitions.Any(definition => string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));

    public static SalesWebPartDefinition? Find(string key)
        => Definitions.FirstOrDefault(definition => string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed record SalesWebPartDefinition(
    string Key,
    string Title,
    string Description,
    string RequiredRole);

public sealed record SalesReportDefinitionDto(
    string Key,
    string Title,
    string Description,
    string RequiredRole,
    bool Allowed);

public sealed record SalesDashboardLayoutNode(
    string Id,
    string Type,
    string? Title = null,
    string? Text = null,
    string? ReportKey = null,
    int Columns = 12,
    int? GridColumns = null,
    bool Visible = true,
    IReadOnlyCollection<SalesDashboardLayoutNode>? Children = null);

public sealed record SalesDashboardLayoutNodeDto(
    string Id,
    string Type,
    string? Title,
    string? Text,
    string? ReportKey,
    int Columns,
    int? GridColumns,
    bool Visible,
    bool Allowed,
    IReadOnlyCollection<SalesDashboardLayoutNodeDto> Children);

public sealed record SalesDashboardLayoutResponse(
    IReadOnlyCollection<SalesDashboardLayoutNodeDto> Nodes,
    IReadOnlyCollection<SalesReportDefinitionDto> AvailableReports,
    bool IsDefault,
    bool CanEdit);

public sealed record SaveSalesDashboardLayoutRequest(
    IReadOnlyCollection<SalesDashboardLayoutNode>? Nodes);

public sealed class SalesDashboardLayoutService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> settingsOptions,
    TenantAdminAccessService tenantAdminAccess)
{
    private const int MaxDepth = 8;
    private const int MaxNodes = 250;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<SalesDashboardLayoutResponse> GetAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(user, cancellationToken);
        return await ToResponseAsync(loaded.Nodes, loaded.IsDefault, user, cancellationToken);
    }

    public async Task<SalesDashboardLayoutResponse> SaveAsync(
        SaveSalesDashboardLayoutRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (!await tenantAdminAccess.IsCurrentTenantAdminAsync(user, cancellationToken))
            throw new UnauthorizedAccessException("Nur Tenant-Administratoren dürfen die Reportseite ändern.");

        var normalized = NormalizeAndValidate(request.Nodes ?? []);
        var value = JsonSerializer.Serialize(normalized, JsonOptions);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        await settingsStore.SetAsync(
            SettingsContext(user),
            SalesWebPartCatalog.SettingKey,
            ApplicationSettingScopes.TenantApp,
            document.RootElement.Clone(),
            ActorName(user),
            cancellationToken);

        return await GetAsync(user, cancellationToken);
    }

    public async Task<SalesDashboardLayoutResponse> GetForDashboardAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(user, cancellationToken);
        return await ToResponseAsync(loaded.Nodes, loaded.IsDefault, user, cancellationToken);
    }

    private async Task<SalesDashboardLayoutResponse> ToResponseAsync(
        IReadOnlyCollection<SalesDashboardLayoutNode> nodes,
        bool isDefault,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var canEdit = await tenantAdminAccess.IsCurrentTenantAdminAsync(user, cancellationToken);
        return new(
            nodes.Select(node => ToDto(node, user)).ToArray(),
            SalesWebPartCatalog.Definitions
                .Select(definition => new SalesReportDefinitionDto(
                    definition.Key,
                    definition.Title,
                    definition.Description,
                    definition.RequiredRole,
                    IsAllowed(definition.RequiredRole, user)))
                .ToArray(),
            isDefault,
            canEdit);
    }

    private async Task<LoadedLayout> LoadAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var values = await settingsStore.LoadAsync(SettingsContext(user), cancellationToken);
        var raw = values
            .Where(value => string.Equals(value.Key, SalesWebPartCatalog.SettingKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Key, SalesWebPartCatalog.LegacySettingKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => string.Equals(value.Key, SalesWebPartCatalog.LegacySettingKey, StringComparison.OrdinalIgnoreCase))
            .Select(value => (JsonElement?)value.Value)
            .LastOrDefault();
        if (!raw.HasValue)
            return new(SalesWebPartCatalog.DefaultLayout, true);

        var json = raw.Value.ValueKind == JsonValueKind.String
            ? raw.Value.GetString()
            : raw.Value.GetRawText();
        if (string.IsNullOrWhiteSpace(json))
            return new(SalesWebPartCatalog.DefaultLayout, true);

        try
        {
            var nodes = JsonSerializer.Deserialize<IReadOnlyCollection<SalesDashboardLayoutNode>>(json, JsonOptions);
            if (nodes is not null)
                return new(EnsureAllReports(NormalizeAndValidate(nodes, requireAllReports: false)), false);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // Try the legacy flat webpart format below.
        }

        try
        {
            var legacy = JsonSerializer.Deserialize<IReadOnlyCollection<LegacyWebPartLayout>>(json, JsonOptions);
            if (legacy is not null)
            {
                var reports = legacy
                    .Where(item => SalesWebPartCatalog.Exists(item.Key))
                    .OrderBy(item => item.Order)
                    .Select(item => new SalesDashboardLayoutNode(
                        $"report-{item.Key.ToLowerInvariant()}",
                        SalesDashboardNodeTypes.Report,
                        SalesWebPartCatalog.Find(item.Key)!.Title,
                        ReportKey: item.Key.ToLowerInvariant(),
                        Columns: Math.Clamp(item.Columns, 1, 12),
                        Visible: item.Visible))
                    .ToArray();
                return new(
                    EnsureAllReports([new("legacy-grid", SalesDashboardNodeTypes.Grid, Title: "Reports", Columns: 12, GridColumns: 12, Children: reports)]),
                    false);
            }
        }
        catch (JsonException)
        {
        }

        return new(SalesWebPartCatalog.DefaultLayout, true);
    }

    private static IReadOnlyCollection<SalesDashboardLayoutNode> NormalizeAndValidate(
        IReadOnlyCollection<SalesDashboardLayoutNode> nodes,
        bool requireAllReports = true)
    {
        var nodeCount = 0;
        var reportKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeChildren(nodes, 0, ref nodeCount, reportKeys);
        if (requireAllReports && reportKeys.Count != SalesWebPartCatalog.Definitions.Count)
            throw new ArgumentException("Die Seite muss jeden verfügbaren Report genau einmal enthalten. Reports können ausgeblendet, aber nicht aus dem Modell entfernt werden.", nameof(nodes));
        return normalized;
    }

    private static IReadOnlyCollection<SalesDashboardLayoutNode> EnsureAllReports(
        IReadOnlyCollection<SalesDashboardLayoutNode> nodes)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = nodes.ToList();
        CollectReportKeys(result, used);
        var missing = SalesWebPartCatalog.Definitions
            .Where(definition => !used.Contains(definition.Key))
            .Select(definition => new SalesDashboardLayoutNode(
                $"report-{definition.Key}",
                SalesDashboardNodeTypes.Report,
                Title: definition.Title,
                ReportKey: definition.Key,
                Columns: SalesWebPartCatalog.DefaultReportSpan(definition.Key),
                Visible: true))
            .ToArray();
        if (missing.Length == 0)
            return result;

        var firstGridIndex = result.FindIndex(node => node.Type == SalesDashboardNodeTypes.Grid);
        if (firstGridIndex >= 0)
        {
            var grid = result[firstGridIndex];
            result[firstGridIndex] = grid with { Children = (grid.Children ?? []).Concat(missing).ToArray() };
        }
        else
        {
            result.Add(new SalesDashboardLayoutNode(
                "additional-reports",
                SalesDashboardNodeTypes.Grid,
                Title: "Weitere Reports",
                Columns: 12,
                GridColumns: 12,
                Children: missing));
        }

        return result;
    }

    private static void CollectReportKeys(
        IReadOnlyCollection<SalesDashboardLayoutNode> nodes,
        HashSet<string> reportKeys)
    {
        foreach (var node in nodes)
        {
            if (node.Type == SalesDashboardNodeTypes.Report && node.ReportKey is not null)
                reportKeys.Add(node.ReportKey);
            CollectReportKeys(node.Children ?? [], reportKeys);
        }
    }

    private static IReadOnlyCollection<SalesDashboardLayoutNode> NormalizeChildren(
        IReadOnlyCollection<SalesDashboardLayoutNode>? children,
        int depth,
        ref int nodeCount,
        HashSet<string> reportKeys)
    {
        if (depth > MaxDepth)
            throw new ArgumentException("Die Reportseite ist zu tief verschachtelt.");

        var result = new List<SalesDashboardLayoutNode>();
        foreach (var original in children ?? [])
        {
            if (++nodeCount > MaxNodes)
                throw new ArgumentException("Die Reportseite enthält zu viele Komponenten.");

            var type = (original.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (!SalesDashboardNodeTypes.All.Contains(type))
                throw new ArgumentException($"Unbekannter Seitentyp: {original.Type}.");

            var id = string.IsNullOrWhiteSpace(original.Id)
                ? $"node-{Guid.NewGuid():N}"
                : original.Id.Trim();
            var title = string.IsNullOrWhiteSpace(original.Title) ? null : original.Title.Trim();
            var text = string.IsNullOrWhiteSpace(original.Text) ? null : original.Text.Trim();
            var reportKey = string.IsNullOrWhiteSpace(original.ReportKey)
                ? null
                : original.ReportKey.Trim().ToLowerInvariant();

            if (type == SalesDashboardNodeTypes.Report)
            {
                if (reportKey is null || !SalesWebPartCatalog.Exists(reportKey))
                    throw new ArgumentException("Die Seite enthält einen unbekannten Report.");
                if (!reportKeys.Add(reportKey))
                    throw new ArgumentException($"Der Report „{reportKey}“ darf nur einmal platziert werden.");
            }
            else if (type is SalesDashboardNodeTypes.Heading or SalesDashboardNodeTypes.Text)
            {
                if (type == SalesDashboardNodeTypes.Heading && string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("Eine Überschrift benötigt einen Text.");
                if (type == SalesDashboardNodeTypes.Text && string.IsNullOrWhiteSpace(text))
                    throw new ArgumentException("Ein Textblock benötigt Inhalt.");
                if (original.Children is { Count: > 0 })
                    throw new ArgumentException("Überschriften und Textblöcke dürfen keine Unterkomponenten enthalten.");
            }

            var normalizedChildren = type is SalesDashboardNodeTypes.Grid or SalesDashboardNodeTypes.Accordion or SalesDashboardNodeTypes.Tabs
                ? NormalizeChildren(original.Children, depth + 1, ref nodeCount, reportKeys)
                : Array.Empty<SalesDashboardLayoutNode>();

            var columns = Math.Clamp(original.Columns, 1, 12);
            var legacyGrid = type == SalesDashboardNodeTypes.Grid && original.GridColumns is null;
            var legacyGridColumns = legacyGrid ? Math.Clamp(original.Columns, 1, 4) : 12;
            int? gridColumns = type == SalesDashboardNodeTypes.Grid
                ? Math.Clamp(original.GridColumns ?? Math.Min(columns, 4), 1, 12)
                : null;
            // Before the 12-column grid was introduced, a grid stored its
            // internal column count in Columns. Preserve that meaning when
            // reading old tenant layouts; new layouts use GridColumns for the
            // internal raster and Columns for the node's width in its parent.
            if (legacyGrid)
            {
                columns = 12;
                normalizedChildren = normalizedChildren
                    .Select(child => child with { Columns = Math.Clamp((int)Math.Round(child.Columns * 12d / legacyGridColumns), 1, 12) })
                    .ToArray();
            }

            result.Add(new SalesDashboardLayoutNode(
                id,
                type,
                title,
                text,
                reportKey,
                columns,
                gridColumns,
                original.Visible,
                normalizedChildren));
        }

        return result;
    }

    private static SalesDashboardLayoutNodeDto ToDto(
        SalesDashboardLayoutNode node,
        ClaimsPrincipal user)
    {
        var allowed = node.Type != SalesDashboardNodeTypes.Report
            || node.ReportKey is not null
            && SalesWebPartCatalog.Find(node.ReportKey) is { } definition
            && IsAllowed(definition.RequiredRole, user);
        return new(
            node.Id,
            node.Type,
            node.Title,
            node.Text,
            node.ReportKey,
            node.Columns,
            node.GridColumns,
            node.Visible,
            allowed,
            (node.Children ?? []).Select(child => ToDto(child, user)).ToArray());
    }

    public static bool IsAllowed(string requiredRole, ClaimsPrincipal user)
        => requiredRole == "sales-user"
            ? HasAnyRole(user, "sales-user", "sales-manager", "sales-management")
            : requiredRole == "sales-cleanup"
                ? HasAnyRole(user, "sales-manager", "sales-management", "sales-backoffice")
                : HasAnyRole(user, "sales-manager", "sales-management");

    public static bool HasAnyRole(ClaimsPrincipal user, params string[] roles)
        => roles.Any(role => IdentityPlatform.Shared.Authorization.TenantApplicationRole.IsInRole(user, role));

    private ApplicationSettingsContext SettingsContext(ClaimsPrincipal user)
        => new(
            settingsOptions.Value.ApplicationKey,
            TenantId(user),
            Guid.Empty,
            user.FindFirstValue("sub") ?? "system:sales-dashboard-layout");

    private static Guid TenantId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId) && tenantId != Guid.Empty
            ? tenantId
            : throw new InvalidOperationException("Der Access Token enthält keine gültige tenant_id.");

    private static string? ActorName(ClaimsPrincipal user)
        => user.FindFirstValue("email")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue("sub");

    private sealed record LoadedLayout(
        IReadOnlyCollection<SalesDashboardLayoutNode> Nodes,
        bool IsDefault);

    private sealed record LegacyWebPartLayout(
        string Key,
        bool Visible,
        int Order,
        int Columns);
}
