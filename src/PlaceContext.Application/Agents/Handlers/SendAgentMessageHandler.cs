using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SendAgentMessageHandler : ICommandHandler<SendAgentMessageCommand, AgentChatSessionView>
{
    private readonly IAgentConfigRepository _configs;
    private readonly IAgentChatSessionRepository _sessions;
    private readonly IChatGateway _chat;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly AgentContextBuilder _contextBuilder;

    public SendAgentMessageHandler(
        IAgentConfigRepository configs,
        IAgentChatSessionRepository sessions,
        IChatGateway chat,
        IUnitOfWork uow,
        IClock clock,
        AgentContextBuilder contextBuilder)
    {
        _configs = configs;
        _sessions = sessions;
        _chat = chat;
        _uow = uow;
        _clock = clock;
        _contextBuilder = contextBuilder;
    }

    public async Task<AgentChatSessionView> HandleAsync(SendAgentMessageCommand command, CancellationToken ct = default)
    {
        // 1. Load config.
        var config = await _configs.GetByProjectIdAsync(command.ProjectId, ct);
        if (config is null || !config.Enabled)
            return DisabledSession(command);

        if (!_chat.IsEnabled)
            return NoModelSession(command);

        // 2. Load or create session.
        AgentChatSession session;
        if (command.SessionId.HasValue)
        {
            session = await _sessions.GetByIdAsync(command.SessionId.Value, ct)
                ?? throw new InvalidOperationException($"Chat session {command.SessionId} not found.");
        }
        else
        {
            session = AgentChatSession.Create(command.ProjectId, null, null, _clock.UtcNow);
            await _sessions.AddAsync(session, ct);
        }

        // 3. Build RAG context from run outputs + graph.
        var context = await _contextBuilder.BuildContextAsync(
            command.ProjectId, command.Message, config.MaxContextChunks, ct);

        // 4. Build the message list for the LLM.
        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt(config, context)),
        };

        // Include conversation history.
        foreach (var m in session.Messages)
            messages.Add(new ChatMessage(m.Role, m.Content));

        messages.Add(new("user", command.Message));

        // 5. Call the chat gateway.
        var settings = new ChatSettings(
            Temperature: config.Temperature,
            TopP: config.TopP);
        var reply = await _chat.ChatAsync(messages, settings, ct);

        // 6. Persist the new messages.
        session.AppendMessages(command.Message, reply, _clock.UtcNow);
        await _sessions.UpdateAsync(session, ct);
        await _uow.SaveChangesAsync(ct);

        return AgentSessionViewMapper.ToView(session);
    }

    private static string BuildSystemPrompt(AgentConfig config, string context)
    {
        var preamble = string.IsNullOrWhiteSpace(config.Preamble) ? AgentConfig.DefaultPreamble : config.Preamble;
        var prompt = preamble + config.SystemPrompt;
        if (string.IsNullOrWhiteSpace(context))
            return prompt;

        return $"{prompt}\n\n## Project context (retrieved automatically)\n\n{context}";
    }

    private static AgentChatSessionView DisabledSession(SendAgentMessageCommand command)
    {
        var session = AgentChatSession.Create(command.ProjectId, null, "Agent disabled", DateTimeOffset.UtcNow);
        session.AppendMessages(command.Message, "The chat agent is disabled for this project. Enable it in the Agents settings.", DateTimeOffset.UtcNow);
        return AgentSessionViewMapper.ToView(session);
    }

    private static AgentChatSessionView NoModelSession(SendAgentMessageCommand command)
    {
        var session = AgentChatSession.Create(command.ProjectId, null, "No model", DateTimeOffset.UtcNow);
        session.AppendMessages(command.Message, "No local language model is configured. Set PlaceContext:Chat:Endpoint to enable the chat agent.", DateTimeOffset.UtcNow);
        return AgentSessionViewMapper.ToView(session);
    }
}
