using System.Text;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Agents.Services;

/// <summary>
/// Runs one unattended "launchpad" agent session: a cron schedule fired, the source table's rows
/// are fetched into the prompt, and the model drives job chains through the
/// <c>[[tool:name|args]]</c> protocol (same shape as the interactive chat page, so sessions
/// render in /chat). Best-effort throughout: a run never throws — failures are recorded as
/// assistant <c>[error: ...]</c> messages in the persisted session memory.
/// </summary>
public sealed class AgentSessionRunner
{
    private const int MaxToolRounds = 5;
    private static readonly TimeSpan DefaultToolTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan JobToolTimeout = TimeSpan.FromMinutes(15);

    private readonly IProjectChatGateway _gateway;
    private readonly CommandAgentOrchestrator _orchestrator;
    private readonly AgentContextBuilder _contextBuilder;
    private readonly IAgentConfigRepository _configs;
    private readonly IProjectDataStore _dataStore;
    private readonly IAgentSessionStore _sessions;
    private readonly LaunchpadToolExecutor _executor;
    private readonly IClock _clock;

    public AgentSessionRunner(
        IProjectChatGateway gateway,
        CommandAgentOrchestrator orchestrator,
        AgentContextBuilder contextBuilder,
        IAgentConfigRepository configs,
        IProjectDataStore dataStore,
        IAgentSessionStore sessions,
        LaunchpadToolExecutor executor,
        IClock clock)
    {
        _gateway = gateway;
        _orchestrator = orchestrator;
        _contextBuilder = contextBuilder;
        _configs = configs;
        _dataStore = dataStore;
        _sessions = sessions;
        _executor = executor;
        _clock = clock;
    }

    /// <summary>
    /// Runs the launchpad to completion and returns the new session id. Never throws: any
    /// failure is captured in the persisted session and the id is still returned.
    /// </summary>
    public async Task<Guid> RunLaunchpadAsync(Guid projectId, string triggerName, string prompt,
        string? sourceTable, Guid chainId, CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid();
        var title = $"🚀 {triggerName}";
        var createdAt = _clock.UtcNow;
        var messages = new List<AgentSessionMessage>();

        try
        {
            // Fetch the source table's rows (best-effort — a fetch failure must not block the run).
            string rowsBlock;
            long? rowCount = null;
            if (sourceTable != null)
            {
                try
                {
                    var page = await _dataStore.QueryTablePageAsync(projectId, sourceTable, null, 1, 200, ct: ct);
                    rowCount = page.TotalCount;
                    rowsBlock = TableRowsJson.Convert(page.Columns, page.Rows);
                }
                catch (Exception ex)
                {
                    rowsBlock = $"table fetch failed: {ex.Message}";
                }
            }
            else
            {
                rowsBlock = "(no source table)";
            }

            var userContent = new StringBuilder();
            userContent.Append(prompt);
            userContent.Append("\n\n```launchpad\n");
            userContent.Append($"Launchpad: {triggerName}\n");
            userContent.Append(sourceTable != null
                ? $"Source table: {sourceTable} ({rowCount?.ToString() ?? "?"} rows)\n"
                : "Source table: none\n");
            userContent.Append($"Rows:\n{rowsBlock}\n");
            userContent.Append($"Target chain: {chainId}\n");
            userContent.Append($"Current UTC time: {_clock.UtcNow:yyyy-MM-dd HH:mm:ss}Z\n");
            userContent.Append("```");
            messages.Add(new AgentSessionMessage("user", userContent.ToString(), _clock.UtcNow));

            await RunToolLoopAsync(projectId, sessionId, title, messages, createdAt,
                userPromptForRag: prompt, channelInstruction: UnattendedInstruction,
                disabledMessage: "No chat model configured — launchpad cannot run.", ct);
        }
        catch (Exception ex)
        {
            messages.Add(new AgentSessionMessage("assistant", $"[error: {ex.Message}]", _clock.UtcNow));
        }

        await PersistAsync(sessionId, projectId, title, messages, createdAt);
        return sessionId;
    }

    /// <summary>
    /// One conversational turn for an external channel (e.g. Slack): appends <paramref name="userText"/>
    /// to an existing session (or starts one), runs the tool loop, and returns the final assistant
    /// text. Never throws — failures become assistant messages and/or a short error string.
    /// </summary>
    public async Task<string> RunChannelTurnAsync(Guid projectId, Guid sessionId, string title,
        string userText, CancellationToken ct = default)
    {
        var existing = await _sessions.GetSessionAsync(sessionId, ct);
        var createdAt = existing?.CreatedAt ?? _clock.UtcNow;
        var messages = existing?.Messages.ToList() ?? new List<AgentSessionMessage>();
        var sessionTitle = string.IsNullOrWhiteSpace(existing?.Title) ? title : existing!.Title;

        messages.Add(new AgentSessionMessage("user", userText, _clock.UtcNow));

        string? finalReply = null;
        try
        {
            finalReply = await RunToolLoopAsync(projectId, sessionId, sessionTitle, messages, createdAt,
                userPromptForRag: userText, channelInstruction: ChannelInstruction,
                disabledMessage: "No chat model configured — I can't reply right now.", ct);
        }
        catch (Exception ex)
        {
            finalReply = $"Sorry — something went wrong: {ex.Message}";
            messages.Add(new AgentSessionMessage("assistant", $"[error: {ex.Message}]", _clock.UtcNow));
        }

        await PersistAsync(sessionId, projectId, sessionTitle, messages, createdAt);
        return finalReply ?? LastAssistantText(messages) ?? "Done.";
    }

    /// <summary>
    /// Shared model/tool loop used by launchpads and external channels. Appends assistant/system
    /// messages onto <paramref name="messages"/>. Returns the final assistant text when the model
    /// stops calling tools (or the disabled/error fallback).
    /// </summary>
    private async Task<string> RunToolLoopAsync(
        Guid projectId, Guid sessionId, string title, List<AgentSessionMessage> messages,
        DateTimeOffset createdAt, string userPromptForRag, string channelInstruction,
        string disabledMessage, CancellationToken ct)
    {
        if (!(await _gateway.GetStatusAsync(projectId, ct)).IsEnabled)
        {
            messages.Add(new AgentSessionMessage("assistant", disabledMessage, _clock.UtcNow));
            return disabledMessage;
        }

        AgentConfig? config = null;
        try { config = await _configs.GetByProjectIdAsync(projectId, ct); }
        catch { /* best-effort: defaults below */ }

        var systemPrompt = (string.IsNullOrWhiteSpace(config?.SystemPrompt) ? DefaultSystemPrompt : config!.SystemPrompt)
            + "\n\n" + (string.IsNullOrWhiteSpace(config?.LaunchpadToolCatalog) ? AgentConfig.DefaultLaunchpadToolCatalog : config!.LaunchpadToolCatalog)
            + "\n\n" + channelInstruction;

        string ragContext = "";
        try
        {
            ragContext = await _contextBuilder.BuildContextAsync(
                projectId, userPromptForRag, config?.MaxContextChunks ?? AgentConfig.DefaultMaxContextChunks, ct);
        }
        catch { /* best-effort */ }
        if (!string.IsNullOrWhiteSpace(ragContext))
            systemPrompt += "\n\n## Project context (retrieved automatically)\n\n" + ragContext;

        var route = await _orchestrator.RouteAsync(projectId, userPromptForRag, ragContext, ct);
        systemPrompt += "\n\n" + route.PromptSection;

        var temperature = config?.Temperature ?? AgentConfig.DefaultTemperature;
        string finalText = "I've completed the requested actions.";

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var chatMessages = BuildChatMessages(systemPrompt, messages);
            var response = await _gateway.ChatAsync(
                projectId, chatMessages, new ChatSettings(Temperature: temperature), ct);

            var toolCalls = AgentToolCallParser.Parse(response);
            if (toolCalls.Count == 0)
            {
                finalText = string.IsNullOrWhiteSpace(response)
                    ? "I've completed the requested actions."
                    : response;
                messages.Add(new AgentSessionMessage("assistant", finalText, _clock.UtcNow));
                break;
            }

            // Execute the round's calls. NO retries — run_job/run_job_chain have side effects.
            var executed = new List<AgentSessionToolCall>();
            var toolResults = new StringBuilder("\n\n## Tool Results\n\n");
            foreach (var (name, args) in toolCalls)
            {
                var timeout = name is AgentToolNames.RunJob or AgentToolNames.RunJobChain ? JobToolTimeout : DefaultToolTimeout;
                string result;
                string status;
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                try
                {
                    result = route.CanUse(name, args)
                        ? await _executor.ExecuteAsync(projectId, name, args, linkedCts.Token)
                        : $"Error: the collaborating agents are not allowed to use {name} with these arguments.";
                    status = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                        ? "Error" : "Completed";
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    result = $"Error: timed out after {(int)timeout.TotalSeconds}s";
                    status = "Error";
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                    status = "Error";
                }

                executed.Add(new AgentSessionToolCall(name, args, status, result, "text"));

                toolResults.Append("### " + name + "\n");
                toolResults.Append("Args: " + args + "\n");
                toolResults.Append(status == "Completed" ? "Result:\n" : "Error:\n");
                toolResults.Append(result);
                toolResults.Append("\n\n");
            }

            // Assistant message carrying the tool calls + system tool-results message
            // (Chat.razor shape so /chat renders the transcript).
            messages.Add(new AgentSessionMessage("assistant",
                AgentToolCallParser.StripToolCalls(response), _clock.UtcNow, executed));
            messages.Add(new AgentSessionMessage("system", toolResults.ToString(), _clock.UtcNow));

            await PersistAsync(sessionId, projectId, title, messages, createdAt);

            if (round == MaxToolRounds - 1)
            {
                finalText = "I hit the tool-round limit before finishing. Check the session in chat for details.";
                messages.Add(new AgentSessionMessage("assistant", finalText, _clock.UtcNow));
            }
        }

        return finalText;
    }

    private static string? LastAssistantText(IReadOnlyList<AgentSessionMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
            if (messages[i].Role == "assistant" && !string.IsNullOrWhiteSpace(messages[i].Content))
                return messages[i].Content;
        return null;
    }

    private const string DefaultSystemPrompt =
        "You are a helpful assistant for this project. Use the provided context to answer questions " +
        "accurately. Answer directly and concisely: no preamble, no restating the question, no " +
        "visible chain-of-thought. Keep answers short unless the user asks for detail.";

    private const string UnattendedInstruction =
        "You are running unattended on a schedule. Never ask clarifying questions — " +
        "decide and act. When finished, reply with a brief summary of what you did.";

    private const string ChannelInstruction =
        "You are replying in Slack. Keep answers short and plain text. Prefer list_chains / " +
        "run_job_chain / list_jobs when the user asks to run work. Never invent chain or job ids — " +
        "look them up first. When finished, reply with a brief summary the user can read in Slack.";

    /// <summary>
    /// System prompt + history with assistant tool calls re-serialized as <c>[[tool:name|args]]</c>
    /// lines and the following tool-results system message merged in — exactly the merge
    /// Chat.razor's BuildChatMessages does. History capped at the last 20 entries.
    /// </summary>
    private static List<ChatMessage> BuildChatMessages(string systemPrompt, List<AgentSessionMessage> memory)
    {
        var result = new List<ChatMessage> { new("system", systemPrompt) };

        var history = new List<ChatMessage>();
        for (var i = 0; i < memory.Count; i++)
        {
            var m = memory[i];

            // Skip standalone system messages (tool results are merged below).
            if (m.Role == "system") continue;

            var content = m.Content;
            if (m.Role == "assistant")
            {
                if (m.ToolCalls is { Count: > 0 })
                {
                    var toolCallText = string.Join("\n", m.ToolCalls.Select(tc =>
                        $"[[tool:{tc.ToolName}|{tc.Args}]]"));
                    content = string.IsNullOrWhiteSpace(content)
                        ? toolCallText
                        : content + "\n\n" + toolCallText;
                }

                // Merge the next message if it's a system tool-results message.
                if (i + 1 < memory.Count && memory[i + 1].Role == "system"
                    && memory[i + 1].Content.Contains("Tool Results"))
                {
                    content += "\n\n" + memory[i + 1].Content;
                    i++;
                }
            }

            history.Add(new ChatMessage(m.Role, content));
        }

        foreach (var m in history.TakeLast(20))
            result.Add(m);
        return result;
    }

    private async Task PersistAsync(Guid sessionId, Guid projectId, string title,
        List<AgentSessionMessage> messages, DateTimeOffset createdAt)
    {
        try
        {
            await _sessions.SaveSessionAsync(sessionId,
                new AgentSessionMemory(sessionId, projectId, title, messages, createdAt, _clock.UtcNow));
        }
        catch { /* best-effort: persistence failure must not fail the run */ }
    }
}
