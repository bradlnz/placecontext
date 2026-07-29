using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BumpDefaultTimeout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TimeoutSeconds",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1800,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 300);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TimeoutSeconds",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 300,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1800);
        }
    }
}
