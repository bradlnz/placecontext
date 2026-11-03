using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCrmIngestionSettingsClientScope : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientId",
            table: "crm_ingestion_settings",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.DropPrimaryKey(
            name: "PK_crm_ingestion_settings",
            table: "crm_ingestion_settings");

        migrationBuilder.AddPrimaryKey(
            name: "PK_crm_ingestion_settings",
            table: "crm_ingestion_settings",
            columns: new[] { "ProjectId", "ClientId" });

        migrationBuilder.CreateIndex(
            name: "IX_crm_ingestion_settings_ProjectId",
            table: "crm_ingestion_settings",
            column: "ProjectId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_crm_ingestion_settings_ProjectId",
            table: "crm_ingestion_settings");

        migrationBuilder.DropPrimaryKey(
            name: "PK_crm_ingestion_settings",
            table: "crm_ingestion_settings");

        migrationBuilder.DropColumn(
            name: "ClientId",
            table: "crm_ingestion_settings");

        migrationBuilder.AddPrimaryKey(
            name: "PK_crm_ingestion_settings",
            table: "crm_ingestion_settings",
            column: "ProjectId");
    }
}
