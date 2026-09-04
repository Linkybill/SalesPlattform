using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConnectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VerificationTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NotifyUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRenewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_subscriptions_TenantId_ProviderKey_ChannelId",
                table: "integration_subscriptions",
                columns: new[] { "TenantId", "ProviderKey", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_subscriptions_TenantId_ProviderKey_ConnectionKe~",
                table: "integration_subscriptions",
                columns: new[] { "TenantId", "ProviderKey", "ConnectionKey", "Module" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_subscriptions_TenantId_Status_ExpiresAt",
                table: "integration_subscriptions",
                columns: new[] { "TenantId", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_subscriptions");
        }
    }
}
