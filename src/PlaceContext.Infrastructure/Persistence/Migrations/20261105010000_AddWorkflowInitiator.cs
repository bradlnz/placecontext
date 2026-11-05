using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105010000_AddWorkflowInitiator")]
public sealed class AddWorkflowInitiator : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(name: "InitiatedBy", table: "chain_runs", type: "uuid", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "InitiatedBy", table: "chain_runs");
}
