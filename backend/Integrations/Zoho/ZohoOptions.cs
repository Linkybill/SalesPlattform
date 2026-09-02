namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoOptions
{
    public string AccountsUrl { get; set; } = "https://accounts.zoho.eu";

    public string ApiUrl { get; set; } = "https://www.zohoapis.eu";

    public string RedirectUri { get; set; } =
        "http://localhost:3101/apps/sales-plattform/api/integrations/zoho/oauth/callback";

    public string FrontendCallbackUrl { get; set; } =
        "http://localhost:3101/apps/sales-plattform/";

    public string Scopes { get; set; } =
        "ZohoCRM.modules.READ,ZohoCRM.users.READ,ZohoCRM.settings.modules.READ,ZohoCRM.settings.fields.READ,ZohoCRM.settings.layouts.READ,ZohoCRM.settings.pipeline.READ,ZohoCRM.settings.related_lists.READ,ZohoCRM.modules.emails.READ";

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
