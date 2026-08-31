using Microsoft.EntityFrameworkCore;
using IdentityPlatform.Shared.Database;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed record HelloWorldDatabaseResult(int StoredRecords, string DatabaseStrategy);

public sealed class HelloWorldService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> databases)
{
    public async Task<HelloWorldDatabaseResult> GetAsync(
        CancellationToken cancellationToken)
    {
        await using var session = await databases.OpenReadOnlyAsync(cancellationToken);
        var storedRecords = await session.Context.HelloWorldRecords
            .AsNoTracking()
            .CountAsync(cancellationToken);

        return new HelloWorldDatabaseResult(
            storedRecords,
            session.Binding.Strategy.ToString());
    }
}
