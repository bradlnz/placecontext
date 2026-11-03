using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260801010000_AddJobTestMethodResults")]
public sealed class AddJobTestMethodResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "MethodResultsJson",
            table: "job_test_cases",
            type: "text",
            nullable: false,
            defaultValue: "[]");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(
            name: "MethodResultsJson",
            table: "job_test_cases");
}
