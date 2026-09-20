using System.Reflection;
using System.Text.Json;
using SalesPlattform.Backend.Integrations.Zoho;

internal static class ReplacementChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        var manual = Method(typeof(ZohoCrmHookUpdateService), "IsManualRebuild");
        foreach (var trigger in new[] { "manual", "Manual" })
            check((bool)manual.Invoke(null, [trigger])!, "Manual runs must replace even unexpired subscriptions.");
        foreach (var trigger in new[] { "schedule", "externalevent", "" })
            check(!(bool)manual.Invoke(null, [trigger])!, "Non-manual runs must retain maintenance semantics.");
        var skipped = Method(typeof(ZohoCrmHookUpdateService), "SkippedResult");
        foreach (var trigger in new[] { "manual", "schedule" })
        {
            var result = (SalesPlattform.Backend.Integrations.Abstractions.CrmHookUpdateResult)skipped.Invoke(null, [trigger, "Hooks disabled"] )!;
            check(result.HasWarnings == (trigger == "manual"), "An explicitly requested rebuild that cannot run must report a warning.");
        }

        var now = DateTimeOffset.UtcNow;
        var parse = Method(typeof(ZohoCrmAdapter), "ParseRegistrationConfirmation");
        var disable = Method(typeof(ZohoCrmAdapter), "ValidateDisableConfirmation");
        JsonElement Confirmation(string code = "SUCCESS", string status = "success", string channel = "222",
            string module = "Calls", string? expiry = null) => JsonSerializer.SerializeToElement(new
            {
                watch = new[] { new { code, status, details = new { events = new[] { new
                { channel_id = channel, resource_name = module, channel_expiry = expiry ?? now.AddDays(1).ToString("O") } } } } }
            });
        var confirmed = (ZohoNotificationRegistration)parse.Invoke(null, [Confirmation(), "222", "Calls", now])!;
        check(confirmed.ChannelId == "222" && confirmed.ExpiresAt > now, "Confirm requested channel/module/expiry before saving.");
        void Reject(Action action)
        {
            try { action(); throw new Exception("Invalid confirmation accepted."); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            { check(true, "Invalid confirmation rejected."); }
        }
        foreach (var bad in new[] { Confirmation(code: "ERROR"), Confirmation(status: "error"), Confirmation(channel: "333"),
            Confirmation(module: "Tasks"), Confirmation(expiry: "invalid"), Confirmation(expiry: now.ToString("O")),
            Confirmation(expiry: now.AddDays(8).ToString("O")), JsonSerializer.SerializeToElement(new { watch = Array.Empty<object>() }) })
            Reject(() => parse.Invoke(null, [bad, "222", "Calls", now]));
        JsonElement Disabled(string code = "SUCCESS", string channel = "111") => JsonSerializer.SerializeToElement(new
        { watch = new[] { new { code, status = "success", details = new { channel_id = channel } } } });
        disable.Invoke(null, [Disabled(), "111"]); check(true, "Confirmed old-channel deletion accepted.");
        Reject(() => disable.Invoke(null, [Disabled(code: "ERROR"), "111"]));
        Reject(() => disable.Invoke(null, [Disabled(channel: "333"), "111"]));
        Reject(() => disable.Invoke(null, [JsonSerializer.SerializeToElement(new { watch = Array.Empty<object>() }), "111"]));

        var replacement = Method(typeof(ZohoCrmAdapter).Assembly.GetType("SalesPlattform.Backend.Integrations.Zoho.ZohoHookReplacement")!, "ExecuteAsync");
        Task<ZohoHookReplacementResult> Replace(string? old,
            Func<CancellationToken, Task<ZohoNotificationRegistration>> register,
            Func<ZohoNotificationRegistration, CancellationToken, Task> persist,
            Func<string, CancellationToken, Task> cleanup, CancellationToken ct = default)
            => (Task<ZohoHookReplacementResult>)replacement.Invoke(null, [old, register, persist, cleanup, ct])!;

        foreach (var fault in new[] { "none", "register", "persist", "cleanup", "register-timeout", "persist-ambiguous" })
        {
            var calls = new List<string>();
            var localChannel = "111";
            var oldDisabled = false;
            var result = await Replace("111", ct =>
            {
                calls.Add("register");
                if (fault == "register") throw new InvalidOperationException("HTTP 401 private-secret-token");
                if (fault == "register-timeout") throw new OperationCanceledException("private-secret-token");
                return Task.FromResult(new ZohoNotificationRegistration("222", now.AddDays(1)));
            }, (registration, ct) =>
            {
                calls.Add("persist");
                if (fault == "persist") throw new InvalidOperationException("private-secret-token");
                localChannel = registration.ChannelId;
                if (fault == "persist-ambiguous") throw new InvalidOperationException("commit response lost private-secret-token");
                return Task.CompletedTask;
            }, (old, ct) =>
            {
                calls.Add("cleanup");
                check(localChannel == "222" && old == "111", "Only delete old channel after new local assignment.");
                if (fault == "cleanup") throw new InvalidOperationException("HTTP 429 private-secret-token");
                oldDisabled = true;
                return Task.CompletedTask;
            });
            check(result.Installed == (fault is "none" or "cleanup"), "Report installed only after confirmed save.");
            check(oldDisabled == (fault == "none"), "Failures before cleanup must not delete old channel.");
            check((result.Warning is null) == (fault == "none"), "Partial failures must surface as warnings.");
            check(!JsonSerializer.Serialize(result).Contains("private-secret-token"), "Replacement errors must not leak secrets.");
            if (fault is "register" or "persist" or "register-timeout")
                check(localChannel == "111", "Old local channel remains valid after pre-commit failure.");
            if (fault is "none" or "cleanup")
                check(calls.SequenceEqual(new[] { "register", "persist", "cleanup" }), "Correct replace order.");
            if (fault == "persist-ambiguous")
                check(localChannel == "222" && !oldDisabled && result.Stage == "persist", "Uncertain commit: delete neither channel.");
        }

        var noOldCleanup = false;
        var newResult = await Replace(null, _ => Task.FromResult(new ZohoNotificationRegistration("222", now.AddDays(1))),
            (_, _) => Task.CompletedTask, (_, _) => { noOldCleanup = true; return Task.CompletedTask; });
        check(newResult.Installed && !noOldCleanup, "First registration must not issue a delete.");
        var savedBadResponse = false;
        var badResult = await Replace("111", _ => Task.FromResult(new ZohoNotificationRegistration("111", now.AddDays(1))),
            (_, _) => { savedBadResponse = true; return Task.CompletedTask; }, (_, _) => Task.CompletedTask);
        check(!badResult.Installed && !savedBadResponse, "Do not reuse or delete the old channel as a new channel.");

        foreach (var cancelAt in new[] { "before", "after-register", "after-persist" })
        {
            using var cts = new CancellationTokenSource();
            if (cancelAt == "before") cts.Cancel();
            var registered = false; var persisted = false; var deleted = false;
            try
            {
                await Replace("111", _ =>
                {
                    registered = true;
                    if (cancelAt == "after-register") cts.Cancel();
                    return Task.FromResult(new ZohoNotificationRegistration("222", now.AddDays(1)));
                }, (_, _) => { persisted = true; cts.Cancel(); return Task.CompletedTask; },
                    (_, _) => { deleted = true; return Task.CompletedTask; }, cts.Token);
                throw new Exception("Cancellation swallowed.");
            }
            catch (OperationCanceledException) { check(true, "Caller cancellation propagated."); }
            check(!deleted, "Cancellation must not trigger unrequested cleanup.");
            check(registered == (cancelAt != "before") && persisted == (cancelAt == "after-persist"), "Cancellation preserves stage boundaries.");
        }
    }
}
