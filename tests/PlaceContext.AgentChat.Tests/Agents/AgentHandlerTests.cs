using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public class AgentHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    // ── GetAgentConfigHandler ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentConfig_returns_default_when_none_exists()
    {
        var repo = new InMemoryAgentConfigRepository();
        var handler = new GetAgentConfigHandler(repo);

        var view = await handler.HandleAsync(new GetAgentConfigQuery(Guid.NewGuid()));

        Assert.Equal("qwen3.5:0.8b", view.BaseModel);
        Assert.False(view.Enabled);
    }

    [Fact]
    public async Task GetAgentConfig_returns_saved_config()
    {
        var repo = new InMemoryAgentConfigRepository();
        var config = AgentConfig.Create(Guid.NewGuid(), T0);
        config.Update("custom-model", "test prompt", AgentConfig.DefaultPreamble, AgentConfig.DefaultToolCatalog,
            AgentConfig.DefaultLaunchpadToolCatalog, 10, 0.5f, 0.8f, true, T0);
        await repo.AddAsync(config);

        var handler = new GetAgentConfigHandler(repo);
        var view = await handler.HandleAsync(new GetAgentConfigQuery(config.ProjectId));

        Assert.Equal("custom-model", view.BaseModel);
        Assert.Equal("test prompt", view.SystemPrompt);
        Assert.Equal(10, view.MaxContextChunks);
        Assert.True(view.Enabled);
    }

    // ── UpdateAgentConfigHandler ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAgentConfig_creates_new_config_when_none_exists()
    {
        var repo = new InMemoryAgentConfigRepository();
        var uow = new RecordingAgentChatUnitOfWork();
        var handler = new UpdateAgentConfigHandler(repo, uow, new FakeClock(T0));

        var projectId = Guid.NewGuid();
        var view = await handler.HandleAsync(new UpdateAgentConfigCommand(
            projectId, "my-model", "You are helpful.", AgentConfig.DefaultPreamble,
            AgentConfig.DefaultToolCatalog, AgentConfig.DefaultLaunchpadToolCatalog,
            8, 0.9f, 0.95f, true));

        Assert.Equal("my-model", view.BaseModel);
        Assert.Equal("You are helpful.", view.SystemPrompt);
        Assert.Equal(8, view.MaxContextChunks);
        Assert.True(view.Enabled);
        Assert.Equal(1, uow.SaveCount);

        // Verify it was persisted.
        var saved = await repo.GetByProjectIdAsync(projectId);
        Assert.NotNull(saved);
        Assert.Equal("my-model", saved!.BaseModel);
    }

    [Fact]
    public async Task UpdateAgentConfig_updates_existing_config()
    {
        var repo = new InMemoryAgentConfigRepository();
        var uow = new RecordingAgentChatUnitOfWork();
        var handler = new UpdateAgentConfigHandler(repo, uow, new FakeClock(T0));

        var projectId = Guid.NewGuid();
        await handler.HandleAsync(new UpdateAgentConfigCommand(
            projectId, "model-v1", "prompt v1", AgentConfig.DefaultPreamble,
            AgentConfig.DefaultToolCatalog, AgentConfig.DefaultLaunchpadToolCatalog,
            5, 0.7f, 0.9f, true));

        var view = await handler.HandleAsync(new UpdateAgentConfigCommand(
            projectId, "model-v2", "prompt v2", "Custom preamble.", "Custom catalog.",
            AgentConfig.DefaultLaunchpadToolCatalog, 10, 0.5f, 0.8f, false));

        Assert.Equal("model-v2", view.BaseModel);
        Assert.False(view.Enabled);
        Assert.Equal(2, uow.SaveCount);

        var saved = await repo.GetByProjectIdAsync(projectId);
        Assert.Equal("model-v2", saved!.BaseModel);
    }

    // ── SendAgentMessageHandler ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAgentMessage_returns_disabled_reply_when_config_disabled()
    {
        var configs = new InMemoryAgentConfigRepository();
        var sessions = new InMemoryAgentChatSessionRepository();
        var chat = new FakeChatGateway();
        var uow = new RecordingAgentChatUnitOfWork();

        var handler = new SendAgentMessageHandler(configs, sessions, chat, uow, new FakeClock(T0), new AgentContextBuilder());

        var projectId = Guid.NewGuid();
        var view = await handler.HandleAsync(new SendAgentMessageCommand(projectId, null, "hello"));

        Assert.Contains("disabled", view.Messages[1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.False(chat.LastMessages.Any()); // should not have called the chat gateway.
    }

    [Fact]
    public async Task SendAgentMessage_returns_no_model_reply_when_gateway_disabled()
    {
        var configs = new InMemoryAgentConfigRepository();
        var sessions = new InMemoryAgentChatSessionRepository();
        var chat = new FakeChatGateway { IsEnabled = false };
        var uow = new RecordingAgentChatUnitOfWork();

        // Create an enabled config.
        var projectId = Guid.NewGuid();
        var config = AgentConfig.Create(projectId, T0);
        await configs.AddAsync(config);

        var handler = new SendAgentMessageHandler(configs, sessions, chat, uow, new FakeClock(T0), new AgentContextBuilder());

        var view = await handler.HandleAsync(new SendAgentMessageCommand(projectId, null, "hello"));

        Assert.Contains("No local language model", view.Messages[1].Content);
    }

    [Fact]
    public async Task SendAgentMessage_calls_gateway_and_persists_messages()
    {
        var configs = new InMemoryAgentConfigRepository();
        var sessions = new InMemoryAgentChatSessionRepository();
        var chat = new FakeChatGateway { ReplyToReturn = "Hello! How can I help?" };
        var uow = new RecordingAgentChatUnitOfWork();

        var projectId = Guid.NewGuid();
        var config = AgentConfig.Create(projectId, T0);
        await configs.AddAsync(config);

        var handler = new SendAgentMessageHandler(configs, sessions, chat, uow, new FakeClock(T0), new AgentContextBuilder());

        var view = await handler.HandleAsync(new SendAgentMessageCommand(projectId, null, "hi there"));

        // Two messages: user + assistant
        Assert.Equal(2, view.Messages.Count);
        Assert.Equal("user", view.Messages[0].Role);
        Assert.Equal("hi there", view.Messages[0].Content);
        Assert.Equal("assistant", view.Messages[1].Role);
        Assert.Equal("Hello! How can I help?", view.Messages[1].Content);

        // Gateway was called with a system prompt + user message.
        Assert.Equal(2, chat.LastMessages.Count);
        Assert.Equal("system", chat.LastMessages[0].Role);
        Assert.Equal("user", chat.LastMessages[1].Role);

        // Session was persisted.
        Assert.Single(await sessions.ListForProjectAsync(projectId));
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task SendAgentMessage_appends_to_existing_session()
    {
        var configs = new InMemoryAgentConfigRepository();
        var sessions = new InMemoryAgentChatSessionRepository();
        var chat = new FakeChatGateway { ReplyToReturn = "reply" };
        var uow = new RecordingAgentChatUnitOfWork();
        var clock = new FakeClock(T0);

        var projectId = Guid.NewGuid();
        var config = AgentConfig.Create(projectId, T0);
        await configs.AddAsync(config);

        var handler = new SendAgentMessageHandler(configs, sessions, chat, uow, clock, new AgentContextBuilder());

        // First message — creates new session.
        var view1 = await handler.HandleAsync(new SendAgentMessageCommand(projectId, null, "first"));
        var sessionId = view1.Id;

        // Second message — continues session.
        clock.UtcNow = T0.AddMinutes(1);
        chat.ReplyToReturn = "second reply";
        var view2 = await handler.HandleAsync(new SendAgentMessageCommand(projectId, sessionId, "second"));

        Assert.Equal(sessionId, view2.Id);
        Assert.Equal(4, view2.Messages.Count); // user1, assistant1, user2, assistant2
        Assert.Equal("first", view2.Messages[0].Content);
        Assert.Equal("second", view2.Messages[2].Content);
    }

    // ── List + Get session queries ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAgentChatSessions_returns_sessions_newest_first()
    {
        var repo = new InMemoryAgentChatSessionRepository();
        var projectId = Guid.NewGuid();

        var older = AgentChatSession.Create(projectId, null, "old", T0);
        var newer = AgentChatSession.Create(projectId, null, "new", T0.AddHours(1));
        newer.AppendMessages("q", "a", T0.AddHours(1));
        await repo.AddAsync(older);
        await repo.AddAsync(newer);

        var handler = new ListAgentChatSessionsHandler(repo);
        var result = await handler.HandleAsync(new ListAgentChatSessionsQuery(projectId));

        Assert.Equal(2, result.Count);
        // Newer should be first.
        Assert.Equal("new", result[0].Title);
    }

    [Fact]
    public async Task GetAgentChatSession_returns_null_for_missing_session()
    {
        var repo = new InMemoryAgentChatSessionRepository();
        var handler = new GetAgentChatSessionHandler(repo);

        var result = await handler.HandleAsync(new GetAgentChatSessionQuery(Guid.NewGuid()));

        Assert.Null(result);
    }
}
