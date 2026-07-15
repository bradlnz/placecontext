using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobChainStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StepJobIdsJson",
                table: "job_chains",
                newName: "StagesJson");

            // Data-preserving backfill: every existing row's column was a flat JSON array of job ids
            // (one step per element). Wrap each element into its own size-1 stage array so the new
            // "array of stages" shape reads exactly as the original linear chain — no rows dropped,
            // no chain re-created. Rows that already look nested (shouldn't exist yet, but leaves any
            // pre-migrated data alone) are skipped.
            migrationBuilder.Sql("""
                UPDATE job_chains
                SET "StagesJson" = (
                    SELECT COALESCE(jsonb_agg(jsonb_build_array(elem)), '[]'::jsonb)
                    FROM jsonb_array_elements("StagesJson"::jsonb) AS elem
                )::text
                WHERE jsonb_typeof("StagesJson"::jsonb) = 'array'
                    AND jsonb_array_length("StagesJson"::jsonb) > 0
                    AND jsonb_typeof(("StagesJson"::jsonb)->0) <> 'array';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort flatten back to one job id per element — a fan-out/join chain authored
            // after this migration loses its parallel grouping on downgrade (it becomes linear), but
            // no step is dropped.
            migrationBuilder.Sql("""
                UPDATE job_chains
                SET "StagesJson" = (
                    SELECT COALESCE(jsonb_agg(job), '[]'::jsonb)
                    FROM jsonb_array_elements("StagesJson"::jsonb) AS stage
                    CROSS JOIN LATERAL jsonb_array_elements(stage) AS job
                )::text
                WHERE jsonb_typeof("StagesJson"::jsonb) = 'array';
                """);

            migrationBuilder.RenameColumn(
                name: "StagesJson",
                table: "job_chains",
                newName: "StepJobIdsJson");
        }
    }
}
