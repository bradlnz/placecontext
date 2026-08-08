using Microsoft.EntityFrameworkCore;
using PlaceContext.AgentChat.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.AgentChat.Tests;

public sealed class AgentChatDbContextTests
{
    [Fact]
    public async Task SaveChangesAsync_UnstampedRows_StampsTenantAndIsolatesReads()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<AgentChatDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var owner = new AgentChatDbContext(
                         options,
                         new FakeCurrentTenant(tenantId)))
        {
            owner.AgentConfigs.Add(new AgentConfigRow
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                CreatedAt = now,
                UpdatedAt = now,
            });
            owner.AgentChatSessions.Add(new AgentChatSessionRow
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                CreatedAt = now,
                UpdatedAt = now,
            });
            owner.McpConnections.Add(new McpConnectionRow
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = "tools",
                Transport = "http",
                CreatedAt = now,
            });
            owner.ChatCommands.Add(new ChatCommandRow
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = "summarize",
                ToolName = "summary",
                CreatedAt = now,
                UpdatedAt = now,
            });

            await owner.SaveChangesAsync();

            Assert.Equal(tenantId, Assert.Single(owner.AgentConfigs.Local).TenantId);
            Assert.Equal(tenantId, Assert.Single(owner.AgentChatSessions.Local).TenantId);
            Assert.Equal(tenantId, Assert.Single(owner.McpConnections.Local).TenantId);
            Assert.Equal(tenantId, Assert.Single(owner.ChatCommands.Local).TenantId);
        }

        await using var otherTenant = new AgentChatDbContext(
            options,
            new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await otherTenant.AgentConfigs.ToListAsync());
        Assert.Empty(await otherTenant.AgentChatSessions.ToListAsync());
        Assert.Empty(await otherTenant.McpConnections.ToListAsync());
        Assert.Empty(await otherTenant.ChatCommands.ToListAsync());
    }
}
