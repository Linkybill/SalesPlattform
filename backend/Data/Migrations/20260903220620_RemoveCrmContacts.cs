using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCrmContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The contact feature is removed deliberately. Use idempotent SQL
            // because older deployments used different FK/index names for
            // these columns. Dropping a column also removes its dependent
            // constraint and index in PostgreSQL.
            migrationBuilder.Sql("""
                DELETE FROM "sales_activity_relations"
                WHERE lower("TargetType") IN ('contact', 'contacts');
                DELETE FROM "sales_appointment_relations"
                WHERE lower("TargetType") IN ('contact', 'contacts');
                DELETE FROM "sales_work_item_relations"
                WHERE lower("TargetType") IN ('contact', 'contacts');

                DELETE FROM "integration_entity_links"
                WHERE lower("InternalEntityType") IN ('contact', 'contacts')
                   OR lower("EntityType") IN ('contact', 'contacts');
                DELETE FROM "integration_raw_records"
                WHERE lower("EntityType") IN ('contact', 'contacts');
                DELETE FROM "integration_field_mappings"
                WHERE lower("SourceEntityType") IN ('contact', 'contacts')
                   OR lower("TargetEntityType") IN ('contact', 'contacts');
                DELETE FROM "integration_writeback_operations"
                WHERE lower("EntityType") IN ('contact', 'contacts');
                DELETE FROM "integration_subscriptions"
                WHERE lower("Module") IN ('contact', 'contacts');
                DELETE FROM "integration_webhook_events"
                WHERE lower("EventType") LIKE '%contact%'
                   OR lower(COALESCE("PayloadJson" ->> 'module', '')) = 'contacts';
                DELETE FROM "integration_sync_cursors"
                WHERE lower("EntityType") IN ('contact', 'contacts');

                UPDATE "zoho_schema_cache"
                SET "AvailableModulesJson" = COALESCE(
                        (
                            SELECT jsonb_agg(item)
                            FROM jsonb_array_elements("AvailableModulesJson") AS module(item)
                            WHERE lower(item #>> '{}') NOT IN ('contact', 'contacts')
                        ),
                        '[]'::jsonb),
                    "FieldsJson" = "FieldsJson" - 'Contact' - 'Contacts' - 'contact' - 'contacts',
                    "LayoutsJson" = "LayoutsJson" - 'Contact' - 'Contacts' - 'contact' - 'contacts',
                    "RelatedListsJson" = COALESCE(
                        (
                            SELECT jsonb_object_agg(parent_key, filtered_lists)
                            FROM (
                                SELECT parent.key AS parent_key,
                                       COALESCE(
                                           jsonb_agg(item.value) FILTER (
                                               WHERE item.value IS NOT NULL
                                                 AND lower(COALESCE(item.value ->> 'api_name', '')) NOT IN ('contact', 'contacts')
                                                 AND lower(COALESCE(item.value ->> 'display_label', '')) NOT IN ('contact', 'contacts')
                                                 AND lower(COALESCE(item.value ->> 'module', '')) NOT IN ('contact', 'contacts')
                                           ),
                                           '[]'::jsonb) AS filtered_lists
                                FROM jsonb_each("RelatedListsJson") AS parent(key, value)
                                LEFT JOIN LATERAL jsonb_array_elements(
                                    CASE
                                        WHEN jsonb_typeof(parent.value) = 'array' THEN parent.value
                                        ELSE '[]'::jsonb
                                    END) AS item(value) ON true
                                GROUP BY parent.key
                            ) AS filtered
                        ),
                        '{}'::jsonb)
                WHERE jsonb_typeof("AvailableModulesJson") = 'array';

                ALTER TABLE IF EXISTS "sales_leads"
                    DROP COLUMN IF EXISTS "ContactId";
                ALTER TABLE IF EXISTS "sales_service_cases"
                    DROP COLUMN IF EXISTS "ContactId";
                ALTER TABLE IF EXISTS "sales_offers"
                    DROP COLUMN IF EXISTS "ContactId";

                DROP TABLE IF EXISTS "sales_customer_relationships" CASCADE;
                DROP TABLE IF EXISTS "sales_contacts" CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContactId",
                table: "sales_service_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContactId",
                table: "sales_offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContactId",
                table: "sales_leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FirstName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MobilePhone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceDeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_contacts_sales_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "sales_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_service_cases_ContactId",
                table: "sales_service_cases",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_offers_ContactId",
                table: "sales_offers",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_leads_ContactId",
                table: "sales_leads",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_contacts_CustomerId",
                table: "sales_contacts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_contacts_TenantId_NormalizedEmail",
                table: "sales_contacts",
                columns: new[] { "TenantId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_contacts_TenantId_NormalizedPhone",
                table: "sales_contacts",
                columns: new[] { "TenantId", "NormalizedPhone" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_leads_sales_contacts_ContactId",
                table: "sales_leads",
                column: "ContactId",
                principalTable: "sales_contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_offers_sales_contacts_ContactId",
                table: "sales_offers",
                column: "ContactId",
                principalTable: "sales_contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_service_cases_sales_contacts_ContactId",
                table: "sales_service_cases",
                column: "ContactId",
                principalTable: "sales_contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
