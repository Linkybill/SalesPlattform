using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace SalesPlattform.Backend.Services;

public static class SalesTeamEndpoints
{
    public static void MapSalesTeamEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams").RequireAuthorization("sales-access");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (Exception ex) when (ex is SalesTeamConflictException or DbUpdateConcurrencyException)
            { return Results.Conflict(new { message = "Das Team wurde inzwischen geändert. Bitte die Übersicht neu laden und das Team erneut öffnen." }); }
            catch (TeamDirectoryUnavailableException)
            { return Results.Json(new { message = "Die Sales-Benutzer konnten nicht geprüft werden. Bitte später erneut versuchen." }, statusCode: 503); }
        });
        group.MapGet("/", async (SalesTeamService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        group.MapGet("/users", async (ClaimsPrincipal user, SalesTeamService service, CancellationToken ct) => Results.Ok(await service.UsersAsync(user, ct)));
        group.MapPost("/", async (SaveSalesTeamRequest request, ClaimsPrincipal user, SalesTeamService service, CancellationToken ct) =>
        {
            var team = await service.SaveAsync(null, request, user, ct);
            return Results.Created($"/api/teams/{team.Id}", team);
        });
        group.MapPut("/{id:guid}", async (Guid id, SaveSalesTeamRequest request, ClaimsPrincipal user, SalesTeamService service, CancellationToken ct)
            => Results.Ok(await service.SaveAsync(id, request, user, ct)));
    }
}
