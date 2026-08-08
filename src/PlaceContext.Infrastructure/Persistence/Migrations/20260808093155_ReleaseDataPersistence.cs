using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseDataPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data now owns these tables and adopts them through its independent migration
            // history. The shared model releases ownership without deleting tenant data.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ownership remains with the Data migration history on downgrade as well.
        }
    }
}
