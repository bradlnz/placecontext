using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260801020000_AddCrmIngestionSettings")]
public sealed class AddCrmIngestionSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "crm_ingestion_settings",
            columns: table => new
            {
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                AllowedOrigin = table.Column<string>(type: "text", nullable: false),
                TokenHash = table.Column<string>(type: "text", nullable: true),
                TokenPrefix = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table => table.PrimaryKey("PK_crm_ingestion_settings", x => x.ProjectId));

        migrationBuilder.CreateIndex(
            name: "IX_crm_ingestion_settings_AllowedOrigin",
            table: "crm_ingestion_settings",
            column: "AllowedOrigin");
        migrationBuilder.CreateIndex(
            name: "IX_crm_ingestion_settings_TokenHash",
            table: "crm_ingestion_settings",
            column: "TokenHash",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "crm_ingestion_settings");
}
