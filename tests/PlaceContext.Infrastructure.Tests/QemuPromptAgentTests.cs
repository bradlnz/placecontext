using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class QemuPromptAgentTests
{
    internal static string Tool(string name, object args, string id = "call-1") => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content = (string?)null,
            tool_calls = new[] { new { id, type = "function", function = new { name, arguments = JsonSerializer.Serialize(args) } } } } } },
    });
    internal sealed class ModelApi(params string[] replies) : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Queue<string> _replies = new(replies);
        public List<JsonNode> Requests { get; } = [];
        public List<string?> Auth { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool WaitForCancellation { get; set; }
        public bool TimeoutRequest { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!);
            Auth.Add(request.Headers.Authorization?.ToString());
            Started.TrySetResult();
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, ct);
            if (TimeoutRequest) throw new OperationCanceledException("model request timeout");
            return new(Status) { Content = new StringContent(_replies.Count > 0 ? _replies.Dequeue() : "{}", Encoding.UTF8, "application/json") };
        }
    }
    internal static QemuEnvironmentOptions OptionsForAgent() => new()
    {
        Agent = new() { Endpoint = "http://model.test/v1/chat/completions", Model = "vision-model", ApiKey = "host-only-secret", MaxSteps = 4 },
    };
    private static Task<byte[]> LiveFrame(CancellationToken ct) => Task.FromResult(Convert.FromBase64String(Png));
    private const string Png = "iVBORw0KGgo=";
    private static Task<QemuEnvironmentResult> Guest(string command, int limit, CancellationToken ct) => Task.FromResult(
        new QemuEnvironmentResult(0, "DONE", ""));

    [Fact]
    public async Task Prompt_drives_tool_observation_loop_and_returns_verified_summary_without_executing_prompt()
    {
        using var api = new ModelApi(Tool("guest_shell", new { command = "echo DONE", description = "Inspect guest state" }),
            Tool("live_desktop", new { }, "call-2"), Tool("finish", new { success = true, summary = "Browser task complete" }, "call-3"));
        var agent = new QemuPromptAgent(api, Options.Create(OptionsForAgent()));
        var commands = new List<string>();
        var result = await agent.RunAsync("Open the browser and verify it", "{\"record\":123}", (command, limit, ct) =>
        { commands.Add(command); return Guest(command, limit, ct); }, default, readLiveFrame: LiveFrame);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Browser task complete", result.Stdout);
        Assert.Equal(3, result.AgentSteps);
        Assert.Equal(2, result.Activity!.Count);
        Assert.Contains("echo DONE", commands);
        Assert.Equal(2, commands.Count); // Readiness check and action only; frames come from the live feed.
        Assert.Equal(new[] { "guest_shell", "live_desktop", "finish" },
            api.Requests[0]["tools"]!.AsArray().Select(tool => tool!["function"]!["name"]!.GetValue<string>()));
        Assert.DoesNotContain("Open the browser and verify it", commands);
        Assert.DoesNotContain(commands, command => command.Contains("host-only-secret"));
        Assert.All(api.Auth, auth => Assert.Equal("Bearer host-only-secret", auth));
        Assert.Contains("record", api.Requests[0].ToJsonString());
        Assert.Contains("Open the browser", api.Requests[0].ToJsonString());
        Assert.Contains("call-1", api.Requests[1]["messages"]!.ToJsonString());
        Assert.Equal(1, api.Requests[2]["messages"]!.AsArray().Count(message => message!["content"] is JsonArray));
        Assert.False(api.Requests[0]["parallel_tool_calls"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Failed_guest_action_can_be_observed_and_retried_by_the_model()
    {
        using var api = new ModelApi(Tool("guest_shell", new { command = "first", description = "Try opening browser" }),
            Tool("guest_shell", new { command = "retry", description = "Retry browser" }, "retry"), Tool("finish", new { success = true, summary = "Recovered" }));
        var agent = new QemuPromptAgent(api, Options.Create(OptionsForAgent()));
        var result = await agent.RunAsync("Open browser", null, (command, limit, ct) => command == "first"
            ? Task.FromResult(new QemuEnvironmentResult(1, "", "Display not ready")) : Guest(command, limit, ct), default, readLiveFrame: LiveFrame);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Display not ready", api.Requests[1].ToJsonString());
        Assert.Equal(1, result.Activity![0].ExitCode);
        Assert.Equal(0, result.Activity[1].ExitCode);
    }

    [Fact]
    public async Task Plain_text_cannot_claim_success_and_iteration_limit_stops_the_loop()
    {
        const string text = """{"choices":[{"message":{"role":"assistant","content":"I completed everything"}}]}""";
        using var api = new ModelApi(text, text);
        var config = OptionsForAgent(); config.Agent.MaxSteps = 2;
        var result = await new QemuPromptAgent(api, Options.Create(config)).RunAsync("Do task", null, Guest, default, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("2-step limit", result.Stderr);
        Assert.Equal(2, api.Requests.Count);
        Assert.Equal(2, result.AgentSteps);
    }

    [Fact]
    public async Task Unsupported_tools_do_not_execute_and_model_errors_do_not_expose_response_secrets()
    {
        using var api = new ModelApi(Tool("host_shell", new { command = "danger" }), "credential material");
        var calls = new List<string>();
        var result = await new QemuPromptAgent(api, Options.Create(OptionsForAgent())).RunAsync("Task", null, (command, limit, ct) =>
        { calls.Add(command); return Guest(command, limit, ct); }, default, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("danger", calls);
        Assert.Contains("Unknown tool", api.Requests[1].ToJsonString());
        Assert.DoesNotContain("credential material", result.Stderr);
        using var denied = new ModelApi("secret-body") { Status = HttpStatusCode.Unauthorized };
        result = await new QemuPromptAgent(denied, Options.Create(OptionsForAgent())).RunAsync("Task", null, Guest, default, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("401", result.Stderr);
        Assert.DoesNotContain("secret-body", result.Stderr);
    }

    [Fact]
    public async Task Missing_model_and_invalid_live_frames_fail_without_a_model_request()
    {
        using var api = new ModelApi();
        var config = OptionsForAgent(); config.Agent.Model = "";
        var agent = new QemuPromptAgent(api, Options.Create(config));
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("Task", null, Guest, default, readLiveFrame: LiveFrame));
        config.Agent.Model = "vision";
        var result = await agent.RunAsync("Task", null, Guest, default, readLiveFrame: _ => Task.FromResult("not-an-image"u8.ToArray()));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(api.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_disconnected_live_feed_fails_without_requesting_guest_screenshots(bool disconnected)
    {
        using var api = new ModelApi();
        var commands = new List<string>();
        var result = await new QemuPromptAgent(api, Options.Create(OptionsForAgent())).RunAsync("Task", null,
            (command, limit, ct) => { commands.Add(command); return Guest(command, limit, ct); }, default,
            readLiveFrame: disconnected ? _ => Task.FromException<byte[]>(new IOException("private connection details")) : null);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("live desktop feed", result.Stderr);
        Assert.DoesNotContain("private connection details", result.Stderr);
        Assert.Single(commands);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task Model_request_timeout_is_a_failed_result_without_claiming_the_workflow_deadline_expired()
    {
        using var api = new ModelApi() { TimeoutRequest = true };
        var result = await new QemuPromptAgent(api, Options.Create(OptionsForAgent())).RunAsync("Task", null, Guest, default, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("model request timed out", result.Stderr);
        Assert.Equal(1, result.AgentSteps);
    }

    [Fact]
    public async Task Replay_records_initial_and_post_action_frames_including_failed_commands()
    {
        using var api = new ModelApi(Tool("guest_shell", new { command = "fail", description = "Open browser" }),
            Tool("live_desktop", new { }), Tool("finish", new { success = false, summary = "Browser failed" }));
        var frames = new List<QemuReplayCapture>();
        var result = await new QemuPromptAgent(api, Options.Create(OptionsForAgent())).RunAsync("Open browser", null,
            (command, limit, ct) => command == "fail" ? Task.FromResult(new QemuEnvironmentResult(7, "", "Failed")) : Guest(command, limit, ct),
            default, (capture, _) => { frames.Add(capture); return Task.CompletedTask; }, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(new[] { 0, 1, 2 }, frames.Select(frame => frame.Frame.Sequence));
        Assert.Equal("Desktop ready", frames[0].Frame.Description);
        Assert.Equal(7, frames[1].Frame.ExitCode);
        Assert.All(frames, frame => { Assert.True(frame.Frame.HasImage); Assert.Equal(Convert.FromBase64String(Png), frame.Png); });
        Assert.True(frames[2].Frame.ElapsedMilliseconds >= frames[0].Frame.ElapsedMilliseconds);
    }

    [Fact]
    public async Task Replay_capture_failure_keeps_an_action_marker_and_does_not_fail_the_task()
    {
        using var api = new ModelApi(Tool("guest_shell", new { command = "echo done", description = "Open browser" }),
            Tool("finish", new { success = true, summary = "Done" }));
        var screens = 0;
        var frames = new List<QemuReplayCapture>();
        var result = await new QemuPromptAgent(api, Options.Create(OptionsForAgent())).RunAsync("Open browser", null,
            Guest,
            default, (capture, _) => { frames.Add(capture); return Task.CompletedTask; },
            readLiveFrame: ct => ++screens > 1 ? Task.FromException<byte[]>(new InvalidOperationException("Feed error")) : LiveFrame(ct));
        Assert.Equal(0, result.ExitCode);
        Assert.True(frames[0].Frame.HasImage);
        Assert.False(frames[1].Frame.HasImage);
        Assert.Null(frames[1].Png);
        Assert.Contains("Live preview frame unavailable", frames[1].Frame.Error);
    }

    [Fact]
    public async Task Cancellation_interrupts_the_model_request()
    {
        using var api = new ModelApi() { WaitForCancellation = true };
        using var cancel = new CancellationTokenSource();
        var task = new QemuPromptAgent(api, Options.Create(OptionsForAgent())).RunAsync("Task", null, Guest, cancel.Token, readLiveFrame: LiveFrame);
        await api.Started.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
