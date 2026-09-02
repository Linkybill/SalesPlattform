using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SalesPlattform.Backend.Data;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations;

[DbContext(typeof(SalesPlattformDbContext))]
[Migration("20260901140000_RemoveLegacyLeadAndStageColumns")]
public partial class RemoveLegacyLeadAndStageColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF to_regclass('public.sales_leads') IS NOT NULL
                   AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_leads'
                      AND column_name = 'CallAttempts')
                   AND NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_leads'
                      AND column_name = 'TotalCallAttempts') THEN
                    ALTER TABLE "sales_leads"
                        RENAME COLUMN "CallAttempts" TO "TotalCallAttempts";
                ELSIF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_leads'
                      AND column_name = 'CallAttempts')
                  AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_leads'
                      AND column_name = 'TotalCallAttempts') THEN
                    UPDATE "sales_leads"
                    SET "TotalCallAttempts" = CASE
                        WHEN "TotalCallAttempts" = 0 THEN COALESCE("CallAttempts", 0)
                        ELSE "TotalCallAttempts"
                    END;
                    ALTER TABLE "sales_leads"
                        DROP COLUMN "CallAttempts";
                END IF;

                IF to_regclass('public.sales_deal_stage_history') IS NOT NULL
                   AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_deal_stage_history'
                      AND column_name = 'StageKey')
                   AND NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_deal_stage_history'
                      AND column_name = 'StageKeySnapshot') THEN
                    ALTER TABLE "sales_deal_stage_history"
                        RENAME COLUMN "StageKey" TO "StageKeySnapshot";
                ELSIF to_regclass('public.sales_deal_stage_history') IS NOT NULL
                  AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_deal_stage_history'
                      AND column_name = 'StageKey')
                  AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_deal_stage_history'
                      AND column_name = 'StageKeySnapshot') THEN
                    UPDATE "sales_deal_stage_history"
                    SET "StageKeySnapshot" = CASE
                        WHEN "StageKeySnapshot" IS NULL OR "StageKeySnapshot" = '' THEN "StageKey"
                        ELSE "StageKeySnapshot"
                    END;
                    ALTER TABLE "sales_deal_stage_history"
                        DROP COLUMN "StageKey";
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF to_regclass('public.sales_leads') IS NOT NULL
                   AND NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_leads'
                      AND column_name = 'CallAttempts') THEN
                    ALTER TABLE "sales_leads"
                        ADD COLUMN "CallAttempts" integer NOT NULL DEFAULT 0;
                    UPDATE "sales_leads"
                    SET "CallAttempts" = COALESCE("TotalCallAttempts", 0);
                    ALTER TABLE "sales_leads"
                        ALTER COLUMN "CallAttempts" DROP DEFAULT;
                END IF;

                IF to_regclass('public.sales_deal_stage_history') IS NOT NULL
                   AND NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_deal_stage_history'
                      AND column_name = 'StageKey')
                   AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'sales_deal_stage_history'
                      AND column_name = 'StageKeySnapshot') THEN
                    ALTER TABLE "sales_deal_stage_history"
                        ADD COLUMN "StageKey" varchar(150) NOT NULL DEFAULT '';
                    UPDATE "sales_deal_stage_history"
                    SET "StageKey" = COALESCE("StageKeySnapshot", '');
                    ALTER TABLE "sales_deal_stage_history"
                        ALTER COLUMN "StageKey" DROP DEFAULT;
                END IF;
            END $$;
            """);
    }
}
