using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using SalesPlattform.Backend.Services;

namespace SalesPlattform.Backend.Strategy;

internal sealed record SalesQuestion(string? Question, SalesChatTurn[]? History)
{
    internal string? ValidationError()
    {
        if (string.IsNullOrWhiteSpace(Question) || Question.Trim().Length > 1200)
            return "Die Frage muss zwischen 1 und 1.200 Zeichen lang sein.";
        if (History is { } history && (history.Length > 8 || history.Any(turn => turn is null
            || turn.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Text) || turn.Text.Length > 3000)))
            return "Der Gesprächsverlauf ist ungültig oder zu lang.";
        return null;
    }
}

internal sealed class SalesChatHub(SalesModelSettings settings, SalesStrategyAgent agent,
    IHttpContextAccessor accessor, ILogger<SalesChatHub> logger) : Hub
{
    public async Task<SalesChatAnswer> Ask(SalesQuestion input)
    {
        var http = Context.GetHttpContext();
        if (http is null || !SalesStrategyEndpoints.HasVerifiedTenant(http))
            throw new HubException("Der aktive Sales-Mandant konnte nicht geprüft werden.");
        if (input?.ValidationError() is { } validation) throw new HubException(validation);
        if (input is null) throw new HubException("Bitte eine Frage eingeben.");

        var previous = accessor.HttpContext;
        try
        {
            if (previous != http) accessor.HttpContext = http;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            var status = await settings.ResolveAsync(SalesStrategyEndpoints.Tenant(http), http.User, deadline.Token);
            if (!status.Enabled || status.Selection is not { } selection)
                throw new HubException("Bitte den Vertriebsassistenten in den Mandanteneinstellungen aktivieren und konfigurieren.");
            deadline.CancelAfter(TimeSpan.FromSeconds(selection.Provider == "ollama" ? 180 : 60));
            return await agent.RespondAsync(selection, input.Question!.Trim(), input.History ?? [], deadline.Token,
                update => Clients.Caller.SendAsync("Progress", update, deadline.Token));
        }
        catch (OperationCanceledException) when (!Context.ConnectionAborted.IsCancellationRequested)
        {
            throw new HubException("Das Modell hat sein Zeitlimit erreicht. Du kannst die Frage erneut senden.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new HubException("Für diese Abfrage fehlt die Sales-Berechtigung.");
        }
        catch (TeamDirectoryUnavailableException)
        {
            throw new HubException("Das Benutzerverzeichnis ist gerade nicht erreichbar.");
        }
        catch (Exception error) when (error is System.ClientModel.ClientResultException or HttpRequestException or JsonException)
        {
            logger.LogWarning("Sales model stream failed ({ErrorType}).", error.GetType().Name);
            throw new HubException("Der Modellanbieter konnte die Frage nicht beantworten.");
        }
        finally
        {
            if (previous != http) accessor.HttpContext = previous;
        }
    }
}
