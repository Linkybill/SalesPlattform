using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesTeamUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Revision",
                table: "sales_teams",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_sales_teams_TenantId_Id",
                table: "sales_teams",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "sales_team_user_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_team_user_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_team_user_members_sales_teams_TenantId_TeamId",
                        columns: x => new { x.TenantId, x.TeamId },
                        principalTable: "sales_teams",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_team_user_members_TenantId_TeamId_MembershipId",
                table: "sales_team_user_members",
                columns: new[] { "TenantId", "TeamId", "MembershipId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_team_user_members");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_sales_teams_TenantId_Id",
                table: "sales_teams");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "sales_teams");
        }
    }
}
