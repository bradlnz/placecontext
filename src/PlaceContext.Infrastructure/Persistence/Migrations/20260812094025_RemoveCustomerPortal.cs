using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomerPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenants_CustomerPortalDomain",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "CustomerPortalDomain",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "CustomerPortalEnabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "CustomerPortalBrandName",
                table: "crm_clients");

            migrationBuilder.DropColumn(
                name: "CustomerPortalDomain",
                table: "crm_clients");

            migrationBuilder.DropColumn(
                name: "CustomerPortalEnabled",
                table: "crm_clients");

            migrationBuilder.DropColumn(
                name: "CustomerPortalLogoUrl",
                table: "crm_clients");

            migrationBuilder.DropColumn(
                name: "CustomerPortalSlug",
                table: "crm_clients");

            migrationBuilder.AddColumn<string>(
                name: "Schema",
                table: "agent_definitions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Schema",
                table: "agent_definitions");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPortalDomain",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerPortalEnabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPortalBrandName",
                table: "crm_clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPortalDomain",
                table: "crm_clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerPortalEnabled",
                table: "crm_clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPortalLogoUrl",
                table: "crm_clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPortalSlug",
                table: "crm_clients",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_CustomerPortalDomain",
                table: "tenants",
                column: "CustomerPortalDomain",
                unique: true);
        }
    }
}
