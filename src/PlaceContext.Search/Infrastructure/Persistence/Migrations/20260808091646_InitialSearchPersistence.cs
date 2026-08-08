using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Search.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSearchPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing gateway databases already contain this table. IF NOT EXISTS adopts that
            // schema into Search's independent migration history without deleting tenant data.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS opensearch_dashboards (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "Name" text NOT NULL,
                    "IndexPattern" text NOT NULL DEFAULT '*',
                    "QueryText" text NULL,
                    "BucketField" text NOT NULL,
                    "BucketType" text NOT NULL DEFAULT 'terms',
                    "ChartType" text NOT NULL DEFAULT 'bar',
                    "MetricType" text NOT NULL DEFAULT 'count',
                    "MetricField" text NULL,
                    "DateInterval" text NULL,
                    "ChartSpecJson" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_opensearch_dashboards" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_opensearch_dashboards_ProjectId_Name"
                    ON opensearch_dashboards ("ProjectId", "Name");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opensearch_dashboards");
        }
    }
}
