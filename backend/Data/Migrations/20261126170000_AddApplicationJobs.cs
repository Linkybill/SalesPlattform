using IdentityPlatform.Shared.Jobs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20261126170000_AddApplicationJobs")]
public sealed class AddApplicationJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ApplicationJobSchema.CreateV1);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ApplicationJobSchema.DropV1);
}
