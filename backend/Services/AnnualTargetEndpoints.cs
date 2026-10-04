using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SalesPlattform.Backend.Services;

public static class AnnualTargetEndpoints
{
    public static void MapAnnualTargetEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports/annual-targets", async (ClaimsPrincipal user, SalesReportService reports, CancellationToken ct) =>
        {
            try { return Results.Ok(await reports.GetAnnualTargetsAsync(user, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (AnnualTargetConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
        }).RequireAuthorization("sales-access");
        app.MapPut("/api/reports/annual-targets", async (SaveAnnualTargetsRequest request, ClaimsPrincipal user, SalesReportService reports, CancellationToken ct) =>
        {
            try { await reports.SaveAnnualTargetsAsync(request, user, ct); return Results.NoContent(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (AnnualTargetConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (Exception ex) when (ex is PostgresException { SqlState: "40001" or "23505" }
                || ex is DbUpdateException { InnerException: PostgresException { SqlState: "40001" or "23505" } })
            { return Results.Conflict(new { message = "Die Zielplanung wurde gleichzeitig geändert. Bitte neu öffnen." }); }
        }).RequireAuthorization("sales-access");
    }
}
