using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseSearchPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Search now owns this table and adopts it through its independent migration history.
            // The shared model releases ownership without deleting tenant data.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ownership remains with the Search migration history on downgrade as well.
        }
    }
}
