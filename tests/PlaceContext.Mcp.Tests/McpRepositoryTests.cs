using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Mcp.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Mcp.Tests;

public sealed class McpRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsConnectionAndOAuthState()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var projectId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var options = new DbContextOptionsBuilder<McpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        Guid connectionId;
        await using (var writeContext = new McpDbContext(options, tenant))
        {
            var connection = McpConnection.Create(
                projectId,
                "tools",
                McpTransport.Http,
                "https://mcp.example.test",
                null,
                null,
                McpAuthType.OAuth,
                null,
                null,
                now);
            connection.SetOAuthCredentials("client-id", "tools.read", now);
            connection.StoreOAuthTokens("access-token", "refresh-token", now.AddHours(1), now);
            connection.RecordConnection("oauth:connected", now.AddMinutes(1));
            connectionId = connection.Id;
            await new EfMcpConnectionRepository(writeContext).AddAsync(connection);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = new McpDbContext(options, tenant);
        var saved = await new EfMcpConnectionRepository(readContext).GetByIdAsync(connectionId);

        Assert.Equal("access-token", saved?.OAuthAccessToken);
        Assert.Equal("oauth:connected", saved?.LastStatus);
        Assert.Equal("client-id", saved?.OAuthClientId);
    }

    [Fact]
    public async Task DbContext_StampsTenantAndIsolatesReads()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<McpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var owner = new McpDbContext(options, new FakeCurrentTenant(tenantId)))
        {
            owner.McpConnections.Add(new McpConnectionRow
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                Name = "tools",
                Transport = McpTransport.Http,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await owner.SaveChangesAsync();
            Assert.Equal(tenantId, Assert.Single(owner.McpConnections.Local).TenantId);
        }

        await using var otherTenant = new McpDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await otherTenant.McpConnections.ToListAsync());
    }
}
