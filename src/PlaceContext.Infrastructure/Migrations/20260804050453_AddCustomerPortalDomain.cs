using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPortalDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerPortalDomain",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_CustomerPortalDomain",
                table: "tenants",
                column: "CustomerPortalDomain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenants_CustomerPortalDomain",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "CustomerPortalDomain",
                table: "tenants");
        }
    }
}
