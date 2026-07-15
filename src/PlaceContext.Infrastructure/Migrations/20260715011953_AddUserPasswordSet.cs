using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PasswordSet",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordSet",
                table: "users");
        }
    }
}
