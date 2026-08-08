using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Data.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDataPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing gateway databases already contain these tables. IF NOT EXISTS adopts them
            // into Data's independent migration history without deleting tenant data.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS data_entities (
                    "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                    "Name" text NOT NULL, "TableName" text NOT NULL, "LabelColumn" text NULL,
                    "RelationsJson" text NOT NULL DEFAULT '[]', "TagsJson" text NOT NULL DEFAULT '[]',
                    "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                    CONSTRAINT "PK_data_entities" PRIMARY KEY ("Id"));
                CREATE INDEX IF NOT EXISTS "IX_data_entities_ProjectId" ON data_entities ("ProjectId");

                CREATE TABLE IF NOT EXISTS data_mappings (
                    "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                    "JobId" uuid NOT NULL, "SourceKind" text NOT NULL DEFAULT 'job',
                    "TargetTable" text NOT NULL, "RowsPath" text NULL,
                    "FieldsJson" text NOT NULL DEFAULT '[]', "Enabled" boolean NOT NULL DEFAULT TRUE,
                    "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL,
                    CONSTRAINT "PK_data_mappings" PRIMARY KEY ("Id"));
                CREATE INDEX IF NOT EXISTS "IX_data_mappings_JobId" ON data_mappings ("JobId");
                CREATE INDEX IF NOT EXISTS "IX_data_mappings_ProjectId" ON data_mappings ("ProjectId");

                CREATE TABLE IF NOT EXISTS entity_tags (
                    "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                    "EntityId" uuid NOT NULL, "EntityName" text NOT NULL, "Key" text NOT NULL,
                    "RunId" uuid NOT NULL, "JobId" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL,
                    CONSTRAINT "PK_entity_tags" PRIMARY KEY ("Id"));
                CREATE INDEX IF NOT EXISTS "IX_entity_tags_EntityId_Key" ON entity_tags ("EntityId", "Key");
                CREATE INDEX IF NOT EXISTS "IX_entity_tags_RunId" ON entity_tags ("RunId");

                CREATE TABLE IF NOT EXISTS project_charts (
                    "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                    "TableName" text NOT NULL, "Html" text NOT NULL, "GeneratedAt" timestamptz NOT NULL,
                    CONSTRAINT "PK_project_charts" PRIMARY KEY ("Id"));
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_project_charts_ProjectId_TableName"
                    ON project_charts ("ProjectId", "TableName");

                CREATE TABLE IF NOT EXISTS record_links (
                    "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                    "Kind" text NOT NULL, "NormalizedValue" text NOT NULL, "DisplayValue" text NOT NULL,
                    "TableName" text NOT NULL, "ColumnName" text NOT NULL, "RowKey" text NOT NULL,
                    "CreatedAt" timestamptz NOT NULL,
                    CONSTRAINT "PK_record_links" PRIMARY KEY ("Id"));
                CREATE INDEX IF NOT EXISTS "IX_record_links_ProjectId_NormalizedValue"
                    ON record_links ("ProjectId", "NormalizedValue");
                CREATE INDEX IF NOT EXISTS "IX_record_links_ProjectId_TableName_RowKey"
                    ON record_links ("ProjectId", "TableName", "RowKey");

                CREATE TABLE IF NOT EXISTS saved_queries (
                    "Id" uuid NOT NULL, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                    "Name" text NOT NULL, "Sql" text NOT NULL,
                    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_saved_queries" PRIMARY KEY ("Id"));
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_saved_queries_ProjectId_Name"
                    ON saved_queries ("ProjectId", "Name");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_entities");

            migrationBuilder.DropTable(
                name: "data_mappings");

            migrationBuilder.DropTable(
                name: "entity_tags");

            migrationBuilder.DropTable(
                name: "project_charts");

            migrationBuilder.DropTable(
                name: "record_links");

            migrationBuilder.DropTable(
                name: "saved_queries");
        }
    }
}
