using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104010000_AddTypeSafeRateBudget")]
public sealed class AddTypeSafeRateBudget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE typesafe_rate_budgets (
            scope text PRIMARY KEY,
            cooldown_until timestamptz NOT NULL DEFAULT '-infinity'
        );
        CREATE TABLE typesafe_rate_admissions (
            scope text NOT NULL REFERENCES typesafe_rate_budgets(scope),
            admitted_at timestamptz NOT NULL,
            tokens integer NOT NULL CHECK (tokens > 0)
        );
        CREATE INDEX ON typesafe_rate_admissions (scope, admitted_at);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE typesafe_rate_admissions;
        DROP TABLE typesafe_rate_budgets;
        """);
}
