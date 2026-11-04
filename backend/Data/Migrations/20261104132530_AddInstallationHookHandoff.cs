using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationHookHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_hook_handoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlacementGeneration = table.Column<long>(type: "bigint", nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConnectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AddressMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetBaseUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RelayConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RelayConfirmedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_hook_handoffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_hook_handoff_channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HandoffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VerificationTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventsJson = table.Column<string>(type: "jsonb", nullable: false),
                    PreviousNotifyUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TargetNotifyUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PatchAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_hook_handoff_channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integration_hook_handoff_channels_integration_hook_handoffs~",
                        column: x => x.HandoffId,
                        principalTable: "integration_hook_handoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_hook_handoff_channels_HandoffId",
                table: "integration_hook_handoff_channels",
                column: "HandoffId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_hook_handoff_channels_TenantId_HandoffId_Module",
                table: "integration_hook_handoff_channels",
                columns: new[] { "TenantId", "HandoffId", "Module" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_hook_handoffs_TenantId_ProviderKey_ConnectionKe~",
                table: "integration_hook_handoffs",
                columns: new[] { "TenantId", "ProviderKey", "ConnectionKey", "TransitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Callback handoff journals are retained customer data. Use a forward migration; rollback must not delete them.");
        }
    }
}
