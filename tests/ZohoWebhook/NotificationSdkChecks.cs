using System.Collections.Concurrent;
using System.Net;
using Com.Zoho.Crm.API.Notifications;
using Com.Zoho.Crm.API.Util;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SalesPlattform.Backend.Integrations.Zoho;

internal static class NotificationSdkChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();
        var received = new ConcurrentDictionary<string, JsonObject>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapPost("/crm/v8/actions/watch", async context =>
        {
            var body = (await JsonNode.ParseAsync(context.Request.Body))!.AsObject();
            var watch = body["watch"]![0]!;
            var channel = watch["channel_id"]!.GetValue<string>();
            check(context.Request.Headers.Authorization == "Zoho-oauthtoken access-" + channel,
                "The SDK must not mix credentials across concurrent tenants.");
            received[channel] = body;
            if (channel == "999")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-API-CREDITS-REMAINING"] = "1234";
            if (channel == "429")
            {
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("""{"code":"TOO_MANY_REQUESTS","details":{},"message":"rate limited","status":"error"}""");
                return;
            }
            await context.Response.WriteAsync(new JsonObject
            {
                ["watch"] = new JsonArray(new JsonObject
                {
                    ["code"] = "SUCCESS", ["status"] = "success", ["message"] = "Subscribed",
                    ["details"] = new JsonObject { ["events"] = new JsonArray(new JsonObject
                    {
                        ["channel_id"] = channel, ["channel_expiry"] = watch["channel_expiry"]!.DeepClone(),
                        ["resource_name"] = "Calls"
                    }) }
                })
            }.ToJsonString());
        });
        await server.StartAsync();
        try
        {
            var domain = server.Urls.Single();
            var sdk = typeof(ZohoCrmAdapter).Assembly.GetType("SalesPlattform.Backend.Integrations.Zoho.ZohoNotificationSdk")!;
            var register = sdk.GetMethod("RegisterAsync", BindingFlags.Public | BindingFlags.Static)!;
            var build = typeof(ZohoCrmAdapter).GetMethod("BuildNotificationRequest", BindingFlags.NonPublic | BindingFlags.Static)!;
            BodyWrapper Payload(string channel) => (BodyWrapper)build.Invoke(null,
                [new Uri("https://sales.example.test/api/integrations/zoho/webhook"), Guid.Parse("00000000-0000-0000-0000-000000000001"),
                 "verification-" + channel, channel, "Calls", false, new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)])!;
            Task<APIResponse<ActionHandler>> Register(string channel, CancellationToken ct = default)
                => (Task<APIResponse<ActionHandler>>)register.Invoke(null, [new ZohoAccessToken("access-" + channel, domain), Payload(channel), ct])!;

            await Task.WhenAll(Enumerable.Range(100, 6).Select(async number =>
            {
                var channel = number.ToString();
                var response = await Register(channel);
                check(response.StatusCode is >= 200 and < 300, "Official SDK request must succeed against the local receiver.");
                check(response.Headers.First(h => h.Key.Equals("X-API-CREDITS-REMAINING", StringComparison.OrdinalIgnoreCase)).Value == "1234", "Usage headers must survive SDK adaptation.");
                check(received[channel]["watch"]![0]!["token"]!.GetValue<string>() == "verification-" + channel,
                    "The SDK must send the original verification token.");
                received[channel]["watch"]![0]!["token"] = "[REDACTED]";
                var redact = typeof(ZohoCrmAdapter).GetMethod("RedactNotificationToken", BindingFlags.NonPublic | BindingFlags.Static)!;
                check(JsonNode.DeepEquals(received[channel], JsonNode.Parse((string)redact.Invoke(null, [Payload(channel)])!)),
                    "The SDK wire JSON must match the payload shown in our diagnostic logs, including expiry and tenant query.");
                var responseBody = JsonNode.Parse(response.ResponseJSON.ToString())!;
                check(responseBody["watch"]![0]!["details"]!["events"]![0]!["channel_id"]!.GetValue<string>() == channel,
                    "Raw SDK response must retain the registration confirmation.");
            }));

            {
                var response = await Register("429");
                check((int)response.StatusCode == 429, "SDK must preserve HTTP errors for existing rate-limit handling.");
                check(response.ResponseJSON.ToString().Contains("TOO_MANY_REQUESTS"), "SDK must preserve error response data.");
            }

            var running = Register("999");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                using var cancel = new CancellationTokenSource();
                var queued = Register("998", cancel.Token);
                cancel.Cancel();
                try { var unexpected = await queued; check(false, "Queued SDK request must cancel."); }
                catch (OperationCanceledException) { check(!received.ContainsKey("998"), "Cancelled queued requests must not be sent."); }
            }
            finally { release.TrySetResult(); }
            var completed = await running;
            check(completed.StatusCode is >= 200 and < 300, "Queued cancellation must not interrupt the active tenant registration.");
        }
        finally
        {
            release.TrySetResult();
            await server.StopAsync();
        }
    }
}
