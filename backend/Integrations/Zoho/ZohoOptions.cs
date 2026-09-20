namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoOptions
{
    public string AccountsUrl { get; set; } = "https://accounts.zoho.eu";

    public string ApiUrl { get; set; } = "https://www.zohoapis.eu";

    public string RedirectUri { get; set; } =
        "https://127.0.0.1:3003/api/integrations/zoho/oauth/callback";

    public string FrontendCallbackUrl { get; set; } =
        "https://127.0.0.1:3003/import";

    /// <summary>
    /// Public HTTPS endpoint Zoho can call. It is intentionally empty for
    /// local development; subscriptions are not registered until a reachable
    /// URL is configured.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    public const string RequiredScopes =
        "ZohoCRM.modules.READ,ZohoCRM.modules.emails.READ,ZohoCRM.modules.tasks.CREATE,ZohoCRM.modules.tasks.UPDATE,ZohoCRM.notifications.CREATE,ZohoCRM.notifications.DELETE,ZohoCRM.notifications.READ,ZohoCRM.users.READ,ZohoCRM.org.READ,ZohoCRM.settings.modules.READ,ZohoCRM.settings.fields.READ,ZohoCRM.settings.layouts.READ,ZohoCRM.settings.pipeline.READ,ZohoCRM.settings.related_lists.READ";

    public string Scopes { get; set; } = RequiredScopes;

    public int OAuthStateLifetimeMinutes { get; set; } = 10;

    public string[] GetScopes()
    {
        // Remove the previously introduced, unsupported grant from old overrides
        // as well as defaults. Module-wide access remains read-only.
        var configured = Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(scope => !scope.Equals("ZohoCRM.modules.DealHistory.READ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (configured.Length == 0)
            return [];

        // Legacy overrides must not drop rights needed by the current adapter.
        // Adding requested scopes does not upgrade an existing OAuth grant.
        return configured
            .Concat(RequiredScopes.Split(','))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public void ValidateForOAuth()
    {
        if (!Uri.TryCreate(AccountsUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Zoho:AccountsUrl ist keine gültige URL.");
        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Zoho:ApiUrl ist keine gültige URL.");
        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out _))
            throw new InvalidOperationException("Zoho:RedirectUri ist keine gültige URL.");
        if (!Uri.TryCreate(FrontendCallbackUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Zoho:FrontendCallbackUrl ist keine gültige URL.");
        if (GetScopes().Length == 0)
            throw new InvalidOperationException("Zoho:Scopes enthält keine Berechtigung.");

        OAuthStateLifetimeMinutes = Math.Clamp(OAuthStateLifetimeMinutes, 1, 30);
        AccountsUrl = AccountsUrl.TrimEnd('/');
        ApiUrl = ApiUrl.TrimEnd('/');
    }
}
