using System.Security.Claims;
using IdentityPlatform.Shared.Jobs;

namespace SalesPlattform.Backend.Services;

public static class SalesTaskPlanningEndpoints
{
    public static void MapSalesTaskPlanningEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sales-tasks").RequireAuthorization("sales-access");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            try { return await next(invocation); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (SalesTaskPlanConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (TeamDirectoryUnavailableException) { return Results.Json(new { message = "Benutzerverzeichnis derzeit nicht erreichbar." }, statusCode: 503); }
        });
        group.MapGet("/mine", async (ClaimsPrincipal user, SalesTaskPlanningService service, CancellationToken ct)
            => Results.Ok(await service.MineAsync(user, ct)));
        group.MapGet("/plans", async (ClaimsPrincipal user, SalesTaskPlanningService service, CancellationToken ct)
            => Results.Ok(await service.ListAsync(user, ct)));
        group.MapPost("/periods", async (SaveSalesTaskPlan request, ClaimsPrincipal user, SalesTaskPlanningService service, CancellationToken ct)
            => Results.Ok(await service.SaveAsync(null, request, user, ct)));
        group.MapPut("/periods/{id:guid}", async (Guid id, SaveSalesTaskPlan request, ClaimsPrincipal user, SalesTaskPlanningService service, CancellationToken ct)
            => Results.Ok(await service.SaveAsync(null, request, user, ct, id)));
        group.MapPost("/periods/{id:guid}/deactivate", async (Guid id, ClaimsPrincipal user, SalesTaskPlanningService service, CancellationToken ct)
            => Results.Ok(await service.DeactivatePeriodAsync(id, user, ct)));
        group.MapPut("/employees/{memberId:guid}", async (Guid memberId, SaveSalesTaskPlan request, ClaimsPrincipal user, SalesTaskPlanningService service, CancellationToken ct)
            => Results.Ok(await service.SaveAsync(memberId, request, user, ct)));
    }
}

public sealed class DistributeSalesTasksJob(SalesTaskPlanningService service) : IPlatformJob
{
    public async Task<PlatformJobResult> ExecuteAsync(PlatformJobExecutionContext context, CancellationToken cancellationToken)
    {
        var assigned = await service.DistributeAsync(DateTimeOffset.UtcNow, cancellationToken);
        return PlatformJobResult.Success($"{assigned} Aufgaben wurden zugewiesen.");
    }
}
