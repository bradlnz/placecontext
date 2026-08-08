using Microsoft.EntityFrameworkCore;
using PlaceContext.AgentChat.Infrastructure.Persistence;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.TestSupport;

namespace PlaceContext.AgentChat.Tests;

public sealed class AgentChatRepositoryTests
{
    [Fact]
    public async Task Repositories_RoundTripOwnedAggregates()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var projectId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);
        var options = new DbContextOptionsBuilder<AgentChatDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        Guid sessionId;
        Guid connectionId;
        Guid commandId;
        await using (var writeContext = new AgentChatDbContext(options, tenant))
        {
            var config = AgentConfig.Create(projectId, now);
            config.Update(
                "agent-model",
                "system prompt",
                "preamble",
                "tools",
                "launchpad tools",
                7,
                0.4f,
                0.8f,
                true,
                now.AddMinutes(1));
            await new EfAgentConfigRepository(writeContext).AddAsync(config);

            var session = AgentChatSession.Create(projectId, Guid.NewGuid(), "Session", now);
            session.AppendMessages("hello", "g'day", now.AddMinutes(1));
            sessionId = session.Id;
            await new EfAgentChatSessionRepository(writeContext).AddAsync(session);

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
            connection.StoreOAuthTokens(
                "access-token",
                "refresh-token",
                now.AddHours(1),
                now);
            connection.RecordConnection("connected", now.AddMinutes(1));
            connectionId = connection.Id;
            await new EfMcpConnectionRepository(writeContext).AddAsync(connection);

            var command = ChatCommand.Create(
                projectId,
                "summarize",
                "Summarize a record",
                "summary",
                "{}",
                now);
            commandId = command.Id;
            await new EfChatCommandRepository(writeContext).AddAsync(command);

            await writeContext.SaveChangesAsync();
        }

        await using var readContext = new AgentChatDbContext(options, tenant);
        var savedConfig = await new EfAgentConfigRepository(readContext)
            .GetByProjectIdAsync(projectId);
        var savedSession = await new EfAgentChatSessionRepository(readContext)
            .GetByIdAsync(sessionId);
        var savedConnection = await new EfMcpConnectionRepository(readContext)
            .GetByIdAsync(connectionId);
        var savedCommand = await new EfChatCommandRepository(readContext)
            .GetByIdAsync(commandId);

        Assert.Equal("agent-model", savedConfig?.BaseModel);
        Assert.Equal("g'day", savedSession?.Messages.Last().Content);
        Assert.Equal("access-token", savedConnection?.OAuthAccessToken);
        Assert.Equal("connected", savedConnection?.LastStatus);
        Assert.Equal("summary", savedCommand?.ToolName);
    }
}
