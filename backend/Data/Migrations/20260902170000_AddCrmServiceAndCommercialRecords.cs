using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260902170000_AddCrmServiceAndCommercialRecords")]
public partial class AddCrmServiceAndCommercialRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE IF EXISTS "sales_contacts"
                ADD COLUMN IF NOT EXISTS "RoleType" varchar(100);

            CREATE TABLE IF NOT EXISTS "sales_service_cases" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "CustomerId" uuid NULL,
                "ContactId" uuid NULL,
                "DealId" uuid NULL,
                "OwnerId" uuid NULL,
                "Subject" varchar(500) NOT NULL,
                "Description" text NULL,
                "Status" varchar(100) NOT NULL,
                "Priority" varchar(50) NOT NULL,
                "Origin" varchar(100) NULL,
                "Reason" varchar(200) NULL,
                "OpenedAt" timestamptz NULL,
                "DueAt" timestamptz NULL,
                "ResolvedAt" timestamptz NULL,
                "SourceCreatedAt" timestamptz NULL,
                "SourceModifiedAt" timestamptz NULL,
                "LastSeenAt" timestamptz NULL,
                "SourceDeletedAt" timestamptz NULL,
                "IsActive" boolean NOT NULL DEFAULT true,
                CONSTRAINT "PK_sales_service_cases" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_sales_service_cases_sales_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_service_cases_sales_contacts_ContactId" FOREIGN KEY ("ContactId") REFERENCES "sales_contacts" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_service_cases_sales_deals_DealId" FOREIGN KEY ("DealId") REFERENCES "sales_deals" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_service_cases_sales_owners_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS "sales_offers" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "CustomerId" uuid NULL,
                "ContactId" uuid NULL,
                "DealId" uuid NULL,
                "OwnerId" uuid NULL,
                "Name" varchar(500) NOT NULL,
                "OfferNumber" varchar(150) NULL,
                "Status" varchar(100) NOT NULL,
                "Amount" numeric(18,2) NULL,
                "Currency" varchar(10) NULL,
                "IssuedAt" timestamptz NULL,
                "SentAt" timestamptz NULL,
                "ValidUntil" timestamptz NULL,
                "SourceCreatedAt" timestamptz NULL,
                "SourceModifiedAt" timestamptz NULL,
                "LastSeenAt" timestamptz NULL,
                "SourceDeletedAt" timestamptz NULL,
                "IsActive" boolean NOT NULL DEFAULT true,
                CONSTRAINT "PK_sales_offers" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_sales_offers_sales_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_offers_sales_contacts_ContactId" FOREIGN KEY ("ContactId") REFERENCES "sales_contacts" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_offers_sales_deals_DealId" FOREIGN KEY ("DealId") REFERENCES "sales_deals" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_offers_sales_owners_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS "sales_orders" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "CustomerId" uuid NULL,
                "OfferId" uuid NULL,
                "DealId" uuid NULL,
                "OwnerId" uuid NULL,
                "Name" varchar(500) NOT NULL,
                "OrderNumber" varchar(150) NULL,
                "Status" varchar(100) NOT NULL,
                "Amount" numeric(18,2) NULL,
                "Currency" varchar(10) NULL,
                "OrderedAt" timestamptz NULL,
                "PromisedAt" timestamptz NULL,
                "DeliveredAt" timestamptz NULL,
                "SourceCreatedAt" timestamptz NULL,
                "SourceModifiedAt" timestamptz NULL,
                "LastSeenAt" timestamptz NULL,
                "SourceDeletedAt" timestamptz NULL,
                "IsActive" boolean NOT NULL DEFAULT true,
                CONSTRAINT "PK_sales_orders" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_sales_orders_sales_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_orders_sales_offers_OfferId" FOREIGN KEY ("OfferId") REFERENCES "sales_offers" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_orders_sales_deals_DealId" FOREIGN KEY ("DealId") REFERENCES "sales_deals" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_orders_sales_owners_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS "sales_invoices" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "CustomerId" uuid NULL,
                "OrderId" uuid NULL,
                "DealId" uuid NULL,
                "OwnerId" uuid NULL,
                "Name" varchar(500) NOT NULL,
                "InvoiceNumber" varchar(150) NULL,
                "Status" varchar(100) NOT NULL,
                "Amount" numeric(18,2) NULL,
                "OpenAmount" numeric(18,2) NULL,
                "Currency" varchar(10) NULL,
                "IssuedAt" timestamptz NULL,
                "DueAt" timestamptz NULL,
                "PaidAt" timestamptz NULL,
                "SourceCreatedAt" timestamptz NULL,
                "SourceModifiedAt" timestamptz NULL,
                "LastSeenAt" timestamptz NULL,
                "SourceDeletedAt" timestamptz NULL,
                "IsActive" boolean NOT NULL DEFAULT true,
                CONSTRAINT "PK_sales_invoices" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_sales_invoices_sales_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "sales_customers" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_invoices_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "sales_orders" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_invoices_sales_deals_DealId" FOREIGN KEY ("DealId") REFERENCES "sales_deals" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_sales_invoices_sales_owners_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "sales_owners" ("Id") ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_sales_service_cases_TenantId_IsActive_Status_DueAt"
                ON "sales_service_cases" ("TenantId", "IsActive", "Status", "DueAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_service_cases_TenantId_CustomerId_OpenedAt"
                ON "sales_service_cases" ("TenantId", "CustomerId", "OpenedAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_offers_TenantId_Status_ValidUntil"
                ON "sales_offers" ("TenantId", "Status", "ValidUntil");
            CREATE INDEX IF NOT EXISTS "IX_sales_offers_TenantId_CustomerId_IssuedAt"
                ON "sales_offers" ("TenantId", "CustomerId", "IssuedAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_orders_TenantId_Status_PromisedAt"
                ON "sales_orders" ("TenantId", "Status", "PromisedAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_orders_TenantId_CustomerId_OrderedAt"
                ON "sales_orders" ("TenantId", "CustomerId", "OrderedAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_invoices_TenantId_Status_DueAt"
                ON "sales_invoices" ("TenantId", "Status", "DueAt");
            CREATE INDEX IF NOT EXISTS "IX_sales_invoices_TenantId_CustomerId_IssuedAt"
                ON "sales_invoices" ("TenantId", "CustomerId", "IssuedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "sales_invoices";
            DROP TABLE IF EXISTS "sales_orders";
            DROP TABLE IF EXISTS "sales_offers";
            DROP TABLE IF EXISTS "sales_service_cases";
            ALTER TABLE IF EXISTS "sales_contacts" DROP COLUMN IF EXISTS "RoleType";
            """);
    }
}
