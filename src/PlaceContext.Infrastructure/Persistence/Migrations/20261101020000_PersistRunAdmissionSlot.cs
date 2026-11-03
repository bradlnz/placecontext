using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

/// <summary>A retry of a request cancelled while being paced must keep its original
/// admission slot, rather than start early just because it already has a ledger row.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261101020000_PersistRunAdmissionSlot")]
public sealed class PersistRunAdmissionSlot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            ALTER TABLE billable_runs ADD COLUMN "DispatchAfter" timestamptz NULL;
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            ALTER TABLE billable_runs DROP COLUMN "DispatchAfter";
            """);
}
