using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaceContext.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseAgentChatPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema ownership moves to AgentChatDbContext. Keep the existing tables intact so the
            // AgentChat initial migration can adopt them without losing gateway-era tenant data.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The physical tables were never removed by Up; rolling back only changes shared
            // migration metadata and leaves AgentChat-owned data untouched.
        }
    }
}
