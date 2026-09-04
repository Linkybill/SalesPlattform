namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoOptions
{
    public string AccountsUrl { get; set; } = "https://accounts.zoho.eu";

    public string ApiUrl { get; set; } = "https://www.zohoapis.eu";

    public string RedirectUri { get; set; } =
        "http://localhost:3101/apps/sales-plattform/api/integrations/zoho/oauth/callback";

    public string FrontendCallbackUrl { get; set; } =
        "http://localhost:3101/apps/sales-plattform/";

    /// <summary>
    /// Public HTTPS endpoint Zoho can call. It is intentionally empty for
    /// local development; subscriptions are not registered until a reachable
    /// URL is configured.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    public string Scopes { get; set; } =
        "ZohoCRM.modules.accounts.READ,ZohoCRM.modules.leads.READ,ZohoCRM.modules.products.READ,ZohoCRM.modules.deals.READ,ZohoCRM.modules.cases.READ,ZohoCRM.modules.quotes.READ,ZohoCRM.modules.salesorders.READ,ZohoCRM.modules.invoices.READ,ZohoCRM.modules.calls.READ,ZohoCRM.modules.tasks.READ,ZohoCRM.modules.events.READ,ZohoCRM.modules.appointments.READ,ZohoCRM.modules.emails.READ,ZohoCRM.modules.tasks.CREATE,ZohoCRM.modules.tasks.UPDATE,ZohoCRM.notifications.CREATE,ZohoCRM.notifications.DELETE,ZohoCRM.users.READ,ZohoCRM.org.READ,ZohoCRM.settings.modules.READ,ZohoCRM.settings.fields.READ,ZohoCRM.settings.layouts.READ,ZohoCRM.settings.pipeline.READ,ZohoCRM.settings.related_lists.READ";

    public int OAuthStateLifetimeMinutes { get; set; } = 10;

    public string[] GetScopes()
        => Scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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
