using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesPlattform.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveContactRelationshipFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "sales_customer_relationships";
                ALTER TABLE IF EXISTS "sales_contacts"
                    DROP COLUMN IF EXISTS "IsPrimary";
                ALTER TABLE IF EXISTS "sales_contacts"
                    DROP COLUMN IF EXISTS "RoleType";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "sales_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RoleType",
                table: "sales_contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_customer_relationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RelationshipType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_customer_relationships", x => x.Id);
                    table.CheckConstraint("CK_sales_customer_relationships_not_self", "\"ParentCustomerId\" <> \"ChildCustomerId\"");
                    table.ForeignKey(
                        name: "FK_sales_customer_relationships_sales_customers_ChildCustomerId",
                        column: x => x.ChildCustomerId,
                        principalTable: "sales_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_customer_relationships_sales_customers_ParentCustomer~",
                        column: x => x.ParentCustomerId,
                        principalTable: "sales_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_customer_relationships_ChildCustomerId",
                table: "sales_customer_relationships",
                column: "ChildCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_customer_relationships_ParentCustomerId",
                table: "sales_customer_relationships",
                column: "ParentCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_customer_relationships_TenantId_ParentCustomerId_Chil~",
                table: "sales_customer_relationships",
                columns: new[] { "TenantId", "ParentCustomerId", "ChildCustomerId", "RelationshipType" },
                unique: true);
        }
    }
}
