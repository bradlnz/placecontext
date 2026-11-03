using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCodeFileSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MapFilesJson",
                table: "jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReduceFilesJson",
                table: "jobs",
                type: "text",
                nullable: true);

            // Backfill: convert legacy single-source code rows into a one-element file set so the new
            // file-set read path is used. The file path is the entrypoint when set, else "main"
            // (matching the single-file CodeWorkload convention). Image rows are left untouched.
            migrationBuilder.Sql("""
                UPDATE jobs
                SET "MapFilesJson" = json_build_array(
                    json_build_object('Path', COALESCE(NULLIF("MapEntrypoint", ''), 'main'), 'Content', "MapSource")
                )::text
                WHERE "MapSourceKind" = 'code' AND "MapSource" IS NOT NULL AND "MapFilesJson" IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE jobs
                SET "ReduceFilesJson" = json_build_array(
                    json_build_object('Path', COALESCE(NULLIF("ReduceEntrypoint", ''), 'main'), 'Content', "ReduceSource")
                )::text
                WHERE "ReduceSourceKind" = 'code' AND "ReduceSource" IS NOT NULL AND "ReduceFilesJson" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MapFilesJson",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "ReduceFilesJson",
                table: "jobs");
        }
    }
}
