using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChainRunStageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The "StepsJson" column's type never changes (it's an opaque text blob — see
            // EfChainRunRepository), only the shape of the objects inside it: fan-out/fan-in adds
            // "stageIndex"/"branchIndex" per step (camelCase — the repository serializes with
            // JsonSerializerDefaults.Web). Every row so far is a linear chain, where each step is its
            // own stage (stageIndex == index) at branch 0, so this backfill is exact, not a guess —
            // no run history or step is dropped. Idempotent: reapplying keeps any value already set.
            migrationBuilder.Sql("""
                UPDATE chain_runs
                SET "StepsJson" = (
                    SELECT COALESCE(jsonb_agg(elem || jsonb_build_object(
                        'stageIndex', COALESCE(elem->'stageIndex', elem->'index'),
                        'branchIndex', COALESCE(elem->'branchIndex', to_jsonb(0))
                    )), '[]'::jsonb)
                    FROM jsonb_array_elements("StepsJson"::jsonb) AS elem
                )::text
                WHERE "StepsJson" IS NOT NULL
                    AND jsonb_typeof("StepsJson"::jsonb) = 'array'
                    AND jsonb_array_length("StepsJson"::jsonb) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE chain_runs
                SET "StepsJson" = (
                    SELECT COALESCE(jsonb_agg(elem - 'stageIndex' - 'branchIndex'), '[]'::jsonb)
                    FROM jsonb_array_elements("StepsJson"::jsonb) AS elem
                )::text
                WHERE "StepsJson" IS NOT NULL
                    AND jsonb_typeof("StepsJson"::jsonb) = 'array'
                    AND jsonb_array_length("StepsJson"::jsonb) > 0;
                """);
        }
    }
}
