using PlaceContext.Application.Agents.Services;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.AgentChat.Integration;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public class AgentSessionRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunLaunchpad_executes_tool_round_then_final_answer()
    {
        var projectId = Guid.NewGuid();
        var chainId = Guid.NewGuid();
        var gateway = new ScriptedChatGateway(
            $"[[tool:run_job_chain|{chainId}|{{\"a\":1}}]]",
            "Done — ran the chain.");
        var executor = new RecordingExecutor { ResultToReturn = "chain ok" };
        var store = new FakeAgentSessionStore();
        var runner = CreateRunner(gateway, executor, store);

        var sessionId = await runner.RunLaunchpadAsync(
            projectId, "nightly-etl", "Process today's orders.", "orders", chainId);

        // Executor called once with the parsed tool + args.
        var call = Assert.Single(executor.Calls);
        Assert.Equal(projectId, call.ProjectId);
        Assert.Equal("run_job_chain", call.Name);
        Assert.Equal($"{chainId}|{{\"a\":1}}", call.Args);

        // Final persisted memory: user, assistant(+toolcalls), system, assistant — in order.
        var memory = store.Saves.Last(s => s.Id == sessionId);
        Assert.Equal($"🚀 nightly-etl", memory.Title);
        Assert.Equal(projectId, memory.ProjectId);
        Assert.Equal(4, memory.Messages.Count);
        Assert.Equal("user", memory.Messages[0].Role);
        Assert.Equal("assistant", memory.Messages[1].Role);
        Assert.Equal("system", memory.Messages[2].Role);
        Assert.Equal("assistant", memory.Messages[3].Role);

        // First user message carries the launchpad context block.
        Assert.Contains("Process today's orders.", memory.Messages[0].Content);
        Assert.Contains("orders", memory.Messages[0].Content);
        Assert.Contains("Source table: orders (2 rows)", memory.Messages[0].Content);
        Assert.Contains("alpha", memory.Messages[0].Content);
        Assert.Contains($"Target chain: {chainId}", memory.Messages[0].Content);

        // Tool call recorded on the assistant message, Chat.razor-shaped results message.
        var tc = Assert.Single(memory.Messages[1].ToolCalls!);
        Assert.Equal("run_job_chain", tc.ToolName);
        Assert.Equal("Completed", tc.Status);
        Assert.Equal("chain ok", tc.Result);
        Assert.Equal("text", tc.ResultType);
        Assert.Contains("## Tool Results", memory.Messages[2].Content);
        Assert.Contains("### run_job_chain", memory.Messages[2].Content);
        Assert.Contains("chain ok", memory.Messages[2].Content);

        // Final assistant message is the model's text answer.
        Assert.Equal("Done — ran the chain.", memory.Messages[3].Content);
    }

    [Fact]
    public async Task RunLaunchpad_gateway_disabled_writes_refusal_message()
    {
        var gateway = new ScriptedChatGateway("unused") { IsEnabled = false };
        var store = new FakeAgentSessionStore();
        var runner = CreateRunner(gateway, new RecordingExecutor(), store);

        var sessionId = await runner.RunLaunchpadAsync(
            Guid.NewGuid(), "nightly-etl", "Do the thing.", null, Guid.NewGuid());

        var memory = Assert.Single(store.Saves, s => s.Id == sessionId);
        Assert.Equal(2, memory.Messages.Count);
        Assert.Equal("user", memory.Messages[0].Role);
        Assert.Equal("assistant", memory.Messages[1].Role);
        Assert.Equal("No chat model configured — launchpad cannot run.", memory.Messages[1].Content);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task RunLaunchpad_executor_throwing_records_error_and_continues()
    {
        var projectId = Guid.NewGuid();
        var chainId = Guid.NewGuid();
        var gateway = new ScriptedChatGateway(
            $"[[tool:run_job_chain|{chainId}]]",
            "Recovered after the failure.");
        var executor = new RecordingExecutor { ExceptionToThrow = new InvalidOperationException("boom") };
        var store = new FakeAgentSessionStore();
        var runner = CreateRunner(gateway, executor, store);

        var sessionId = await runner.RunLaunchpadAsync(
            projectId, "nightly-etl", "Run it.", null, chainId);

        // The loop continued to a second gateway round despite the tool failure.
        Assert.Equal(2, gateway.Calls.Count);

        var memory = store.Saves.Last(s => s.Id == sessionId);
        Assert.Equal(4, memory.Messages.Count);
        var tc = Assert.Single(memory.Messages[1].ToolCalls!);
        Assert.Equal("Error", tc.Status);
        Assert.Contains("boom", tc.Result);
        Assert.Equal("Recovered after the failure.", memory.Messages[3].Content);
    }

    [Fact]
    public async Task RunChannelTurn_continues_existing_session_and_returns_final_text()
    {
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var store = new FakeAgentSessionStore();
        store.Saves.Add(new AgentSessionMemory(
            sessionId, projectId, "💬 Slack C1",
            new List<AgentSessionMessage>
            {
                new("user", "hi", T0),
                new("assistant", "hello", T0),
            },
            T0, T0));

        var gateway = new ScriptedChatGateway("Plain reply from channel.");
        var runner = CreateRunner(gateway, new RecordingExecutor(), store);

        var reply = await runner.RunChannelTurnAsync(projectId, sessionId, "💬 Slack C1", "run the report");

        Assert.Equal("Plain reply from channel.", reply);
        var memory = store.Saves.Last(s => s.Id == sessionId);
        Assert.Equal(4, memory.Messages.Count);
        Assert.Equal("user", memory.Messages[2].Role);
        Assert.Contains("run the report", memory.Messages[2].Content);
        Assert.Equal("assistant", memory.Messages[3].Role);
        Assert.Equal("Plain reply from channel.", memory.Messages[3].Content);
    }

    private static AgentSessionRunner CreateRunner(
        ScriptedChatGateway gateway, RecordingExecutor executor, FakeAgentSessionStore store)
    {
        var workspace = new FakeAgentChatWorkspaceClient
        {
            TablePageToReturn = new AgentChatTablePage(
                new[] { "id", "name" },
                new IReadOnlyList<string?>[]
                {
                    new string?[] { "1", "alpha" },
                    new string?[] { "2", "beta" },
                },
                TotalCount: 2),
        };
        return new AgentSessionRunner(
            gateway,
            new AgentContextBuilder(workspace),
            new InMemoryAgentConfigRepository(),
            workspace,
            store,
            executor,
            new FakeClock(T0));
    }

    // ── Fakes ────────────────────────────────────────────────────────────────────────────────

    private sealed class ScriptedChatGateway : IChatGateway
    {
        private readonly Queue<string> _responses;

        public ScriptedChatGateway(params string[] responses) => _responses = new Queue<string>(responses);

        public bool IsEnabled { get; set; } = true;
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = new();

        public Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
        {
            Calls.Add(messages);
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : "final answer");
        }
    }

    private sealed class RecordingExecutor : LaunchpadToolExecutor
    {
        public RecordingExecutor() : base(null!) { }

        public List<(Guid ProjectId, string Name, string Args)> Calls { get; } = new();
        public string ResultToReturn { get; set; } = "ok";
        public Exception? ExceptionToThrow { get; set; }

        public override Task<string> ExecuteAsync(Guid projectId, string toolName, string args, CancellationToken ct)
        {
            Calls.Add((projectId, toolName, args));
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class FakeAgentSessionStore : IAgentSessionStore
    {
        public List<AgentSessionMemory> Saves { get; } = new();

        public Task<AgentSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
            => Task.FromResult(Saves.LastOrDefault(s => s.Id == sessionId));

        public Task SaveSessionAsync(Guid sessionId, AgentSessionMemory memory, CancellationToken ct = default)
        {
            Saves.Add(memory);
            return Task.CompletedTask;
        }
    }

}
