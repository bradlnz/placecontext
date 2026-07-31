using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260801013000_AddEmailTwoFactorChallenges")]
public sealed class AddEmailTwoFactorChallenges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TwoFactorCodeHash",
            table: "users",
            type: "text",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "TwoFactorCodeExpiresAt",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "TwoFactorCodeLastSentAt",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "TwoFactorCodeFailedAttempts",
            table: "users",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TwoFactorCodeHash", table: "users");
        migrationBuilder.DropColumn(name: "TwoFactorCodeExpiresAt", table: "users");
        migrationBuilder.DropColumn(name: "TwoFactorCodeLastSentAt", table: "users");
        migrationBuilder.DropColumn(name: "TwoFactorCodeFailedAttempts", table: "users");
    }
}
