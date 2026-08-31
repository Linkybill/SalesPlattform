using IdentityPlatform.Shared.Database;

namespace SalesPlattform.Backend.Data;

public sealed class HelloWorldRecord : PlatformTenantEntity
{
    public Guid Id { get; set; }

    public required string Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
