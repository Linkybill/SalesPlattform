using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260902160000_AddSalesNotificationDelivery")]
public partial class AddSalesNotificationDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AttemptCount",
            table: "sales_notifications",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "BodyHtml",
            table: "sales_notifications",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Channel",
            table: "sales_notifications",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "email");

        migrationBuilder.AddColumn<string>(
            name: "LastError",
            table: "sales_notifications",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LockedUntil",
            table: "sales_notifications",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NotificationKey",
            table: "sales_notifications",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAt",
            table: "sales_notifications",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RecipientEmail",
            table: "sales_notifications",
            type: "character varying(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Subject",
            table: "sales_notifications",
            type: "character varying(998)",
            maxLength: 998,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "sales_notifications"
            SET "NotificationKey" = 'legacy:' || "Id"::text
            WHERE "NotificationKey" = '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_sales_notifications_TenantId_NotificationKey",
            table: "sales_notifications",
            columns: new[] { "TenantId", "NotificationKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_notifications_TenantId_DeliveryStatus_LockedUntil",
            table: "sales_notifications",
            columns: new[] { "TenantId", "DeliveryStatus", "LockedUntil" });

        // The initial domain migration created this index through idempotent SQL.
        // Some existing databases therefore legitimately do not have it (for
        // example after a previous schema repair). Keep the migration
        // repeatable instead of failing the whole tenant migration on a
        // missing legacy index.
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_sales_notifications_TenantId_RecipientSubject_DeliveryStatus_DueAt";
            DROP INDEX IF EXISTS "IX_sales_notifications_TenantId_RecipientSubject_DeliveryStatus";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_sales_notifications_TenantId_RecipientSubject_DeliveryStatus_NextAttemptAt",
            table: "sales_notifications",
            columns: new[] { "TenantId", "RecipientSubject", "DeliveryStatus", "NextAttemptAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_sales_notifications_TenantId_NotificationKey";
            DROP INDEX IF EXISTS "IX_sales_notifications_TenantId_DeliveryStatus_LockedUntil";
            DROP INDEX IF EXISTS "IX_sales_notifications_TenantId_RecipientSubject_DeliveryStatus_NextAttemptAt";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_sales_notifications_TenantId_RecipientSubject_DeliveryStatus_DueAt",
            table: "sales_notifications",
            columns: new[] { "TenantId", "RecipientSubject", "DeliveryStatus", "DueAt" });

        migrationBuilder.DropColumn(name: "AttemptCount", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "BodyHtml", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "Channel", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "LastError", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "LockedUntil", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "NotificationKey", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "NextAttemptAt", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "RecipientEmail", table: "sales_notifications");
        migrationBuilder.DropColumn(name: "Subject", table: "sales_notifications");
    }
}
