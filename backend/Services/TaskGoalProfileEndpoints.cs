using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace SalesPlattform.Backend.Services;

public static class TaskGoalProfileEndpoints
{
    public static void MapTaskGoalProfileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/task-goal-profiles").RequireAuthorization("sales-access");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (Exception ex) when (ex is TaskGoalProfileConflictException or DbUpdateConcurrencyException)
            { return Results.Conflict(new { message = "Das Profil wurde inzwischen geändert. Bitte abbrechen, die Übersicht neu laden und das Profil erneut öffnen." }); }
        });
        group.MapGet("/types", () => Results.Ok(TaskGoalProfileService.Types));
        group.MapGet("/industries", async (TaskGoalProfileService service, CancellationToken ct) => Results.Ok(await service.IndustriesAsync(ct)));
        group.MapGet("/", async (TaskGoalProfileService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
        group.MapPost("/", async (SaveTaskGoalProfileRequest request, ClaimsPrincipal user, TaskGoalProfileService service, CancellationToken ct) =>
        {
            var profile = await service.SaveAsync(null, request, user, ct);
            return Results.Created($"/api/task-goal-profiles/{profile.Id}", profile);
        });
        group.MapPut("/{id:guid}", async (Guid id, SaveTaskGoalProfileRequest request, ClaimsPrincipal user, TaskGoalProfileService service, CancellationToken ct)
            => Results.Ok(await service.SaveAsync(id, request, user, ct)));
    }
}
