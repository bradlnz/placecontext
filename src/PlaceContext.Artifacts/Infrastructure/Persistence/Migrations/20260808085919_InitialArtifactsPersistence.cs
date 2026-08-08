using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Artifacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialArtifactsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing gateway databases already contain these tables. IF NOT EXISTS adopts that
            // schema into Artifacts' independent migration history without deleting tenant data.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS job_run_artifacts (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "RunId" uuid NOT NULL,
                    "JobId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL,
                    "Kind" text NOT NULL,
                    "Title" text NOT NULL,
                    "Bucket" text NOT NULL,
                    "ObjectKey" text NOT NULL,
                    "ContentType" text NOT NULL,
                    "SizeBytes" bigint NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "OcrProcessedAt" timestamp with time zone NULL,
                    "OcrError" text NULL,
                    CONSTRAINT "PK_job_run_artifacts" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_job_run_artifacts_RunId"
                    ON job_run_artifacts ("RunId");
                CREATE INDEX IF NOT EXISTS ix_job_run_artifacts_ocr
                    ON job_run_artifacts ("OcrProcessedAt")
                    WHERE "OcrProcessedAt" IS NULL;

                CREATE TABLE IF NOT EXISTS artifact_share_tokens (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "ArtifactId" uuid NOT NULL,
                    "TokenHash" character varying(64) NOT NULL,
                    "TokenPrefix" character varying(20) NOT NULL,
                    "CreatedByUserId" uuid NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "ExpiresAt" timestamp with time zone NOT NULL,
                    "RevokedAt" timestamp with time zone NULL,
                    "LastAccessedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_artifact_share_tokens" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_artifact_share_tokens_job_run_artifacts_ArtifactId"
                        FOREIGN KEY ("ArtifactId") REFERENCES job_run_artifacts ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_artifact_share_tokens_ArtifactId"
                    ON artifact_share_tokens ("ArtifactId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_artifact_share_tokens_TokenHash"
                    ON artifact_share_tokens ("TokenHash");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifact_share_tokens");

            migrationBuilder.DropTable(
                name: "job_run_artifacts");
        }
    }
}
