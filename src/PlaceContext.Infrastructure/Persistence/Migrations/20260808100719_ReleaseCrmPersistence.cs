using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations;

/// <summary>
/// Transfers CRM table ownership to CrmDbContext without dropping gateway data.
/// </summary>
public partial class ReleaseCrmPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
