using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskGoalProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_task_goal_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DailyCapacityMinutes = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_task_goal_profiles", x => x.Id);
                    table.UniqueConstraint("AK_sales_task_goal_profiles_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "sales_task_goal_profile_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SharePercent = table.Column<int>(type: "integer", nullable: false),
                    MinutesPerTask = table.Column<int>(type: "integer", nullable: false),
                    DailyTarget = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_task_goal_profile_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_task_goal_profile_items_sales_task_goal_profiles_Tena~",
                        columns: x => new { x.TenantId, x.ProfileId },
                        principalTable: "sales_task_goal_profiles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_goal_profile_items_TenantId_ProfileId_TaskType",
                table: "sales_task_goal_profile_items",
                columns: new[] { "TenantId", "ProfileId", "TaskType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_task_goal_profiles_TenantId_Name",
                table: "sales_task_goal_profiles",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_task_goal_profile_items");

            migrationBuilder.DropTable(
                name: "sales_task_goal_profiles");
        }
    }
}
