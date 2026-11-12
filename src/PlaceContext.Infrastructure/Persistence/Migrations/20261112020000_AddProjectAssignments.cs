using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112020000_AddProjectAssignments")]
public sealed class AddProjectAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid[]>(name: "AssignedProjectIds", table: "users", type: "uuid[]", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "AssignedProjectIds", table: "users");
}
