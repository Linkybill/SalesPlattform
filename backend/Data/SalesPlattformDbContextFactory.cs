using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SalesPlattform.Backend.Data;

public sealed class SalesPlattformDbContextFactory
    : IDesignTimeDbContextFactory<SalesPlattformDbContext>
{
    public SalesPlattformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SalesPlattformDbContext>()
            .UseNpgsql("Host=localhost;Database=salesplattform_design;Username=postgres;Password=postgres")
            .Options;

        return new SalesPlattformDbContext(options);
    }
}
