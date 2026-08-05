using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmClientPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "CustomerPortalSlug",
                table: "crm_clients",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPortalDomain",
                table: "crm_clients");

            migrationBuilder.DropColumn(
                name: "CustomerPortalEnabled",
                table: "crm_clients");

            migrationBuilder.DropColumn(
                name: "CustomerPortalSlug",
                table: "crm_clients");
        }
    }
}
