using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesTaskPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Industries",
                table: "sales_task_goal_profiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "sales_leads",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_task_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskType = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Industry = table.Column<string>(type: "text", nullable: true),
                    AssignedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_task_assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sales_task_goal_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Configuration = table.Column<string>(type: "jsonb", nullable: false),
                    Audience = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_task_goal_plans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_assignments_TenantId_AssignedOn_TargetType_Targe~",
                table: "sales_task_assignments",
                columns: new[] { "TenantId", "AssignedOn", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_assignments_TenantId_MembershipId_AssignedOn",
                table: "sales_task_assignments",
                columns: new[] { "TenantId", "MembershipId", "AssignedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_assignments_TenantId_WorkItemId",
                table: "sales_task_assignments",
                columns: new[] { "TenantId", "WorkItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_goal_plans_TenantId_MembershipId",
                table: "sales_task_goal_plans",
                columns: new[] { "TenantId", "MembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_goal_plans_TenantId_StartsOn_EndsOn",
                table: "sales_task_goal_plans",
                columns: new[] { "TenantId", "StartsOn", "EndsOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_task_assignments");

            migrationBuilder.DropTable(
                name: "sales_task_goal_plans");

            migrationBuilder.DropColumn(
                name: "Industries",
                table: "sales_task_goal_profiles");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "sales_leads");
        }
    }
}
