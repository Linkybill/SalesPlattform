using Com.Zoho.API.Authenticator;
using Com.Zoho.API.Authenticator.Store;
using Com.Zoho.Crm.API;
using Com.Zoho.Crm.API.Dc;
using Com.Zoho.Crm.API.Notifications;
using Com.Zoho.Crm.API.Util;

namespace SalesPlattform.Backend.Integrations.Zoho;

/// <summary>Official SDK transport for watch registration; credentials remain owned by our tenant token service.</summary>
internal static class ZohoNotificationSdk
{
    // The SDK uses process-global initialization and synchronous HTTP. All SDK calls must
    // pass this gate. Do not use SwitchUser: its ThreadLocal state leaks across pooled tasks.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<APIResponse<ActionHandler>> RegisterAsync(
        ZohoAccessToken accessToken, BodyWrapper request, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // Await the actual synchronous call, including after cancellation. Releasing the
            // gate early would permit another tenant to replace the active SDK credentials.
            return await Task.Run(() => Register(accessToken, request, cancellationToken), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static APIResponse<ActionHandler> Register(
        ZohoAccessToken accessToken, BodyWrapper request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = new BorrowedAccessToken(accessToken.Value);
        try
        {
            new Initializer.Builder()
                .Environment(new DataCenter.Environment(accessToken.ApiDomain.TrimEnd('/'), null, null))
                .Token(token)
                .Store(new NoTokenStore())
                // SDK 4.0.0 multiplies Timeout by 1000 in APIHTTPConnector (seconds).
                .SDKConfig(new SDKConfig.Builder().AutoRefreshFields(false)
                    .PickListValidation(false).UpdateAPIDomain(false).Timeout(30).Build())
                .Initialize(); // Default SDK logger is OFF; no raw tokens or CRM payloads on disk.

            cancellationToken.ThrowIfCancellationRequested();
            var result = new NotificationsOperations().EnableNotifications(request);
            if (result is null)
                throw new HttpRequestException("Zoho SDK lieferte keine HTTP-Antwort.");

            return result;
        }
        catch (Com.Zoho.API.Exception.SDKException)
        {
            // SDK exceptions may contain request data. Keep them out of the central exception logs.
            throw new HttpRequestException("Zoho SDK konnte die Registrierung nicht abschließen (SDK-/Transportfehler).");
        }
        finally
        {
            // Initializer retains the token object globally; clear its credential even on failure.
            token.Remove();
        }
    }

    private sealed class BorrowedAccessToken(string value) : IToken
    {
        private string? accessToken = value;
        public void Authenticate(APIHTTPConnector connection)
            => connection.AddHeader("Authorization", "Zoho-oauthtoken " +
                (accessToken ?? throw new InvalidOperationException("SDK-Token ist nicht mehr aktiv.")));
        public void GenerateToken() { } // Refresh is handled by ZohoTokenService and the central Vault.
        public void Remove() => accessToken = null;
        public string GetId() => "borrowed-access-token";
        public bool Revoke(string id) => throw new NotSupportedException();
    }

    // Providing a store prevents the SDK from creating its default local token file.
    private sealed class NoTokenStore : ITokenStore
    {
        public IToken FindToken(IToken token) => throw new NotSupportedException();
        public IToken FindTokenById(string id) => throw new NotSupportedException();
        public void SaveToken(IToken token) => throw new NotSupportedException();
        public void DeleteToken(string id) => throw new NotSupportedException();
        public List<IToken> GetTokens() => [];
        public void DeleteTokens() => throw new NotSupportedException();
    }
}
