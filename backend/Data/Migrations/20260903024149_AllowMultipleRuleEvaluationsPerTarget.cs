using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260903024149_AllowMultipleRuleEvaluationsPerTarget")]
public partial class AllowMultipleRuleEvaluationsPerTarget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_sales_rule_evaluations_TenantId_RuleRunId_TargetType_Target~";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_rule_evaluations_rule_target"
                ON "sales_rule_evaluations"
                    ("TenantId", "RuleRunId", "RuleDefinitionId", "TargetType", "TargetId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_sales_rule_evaluations_rule_target";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_rule_evaluations_TenantId_RuleRunId_TargetType_Target~"
                ON "sales_rule_evaluations"
                    ("TenantId", "RuleRunId", "TargetType", "TargetId");
            """);
    }
}
