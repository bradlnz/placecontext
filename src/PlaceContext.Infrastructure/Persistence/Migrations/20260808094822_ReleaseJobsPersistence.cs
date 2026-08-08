using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations;

/// <summary>
/// Transfers Jobs table ownership to JobsDbContext. The tables are deliberately preserved;
/// Jobs' initial migration adopts the existing schema into its independent history.
/// </summary>
public partial class ReleaseJobsPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
