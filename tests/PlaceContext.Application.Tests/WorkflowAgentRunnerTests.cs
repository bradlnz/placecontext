using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using System.Text.Json.Nodes;
using PlaceContext.Application.Access;
using PlaceContext.Application.Build;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Tests;

public sealed class WorkflowAgentRunnerTests
{
    private static readonly Guid Connection = Guid.NewGuid();
    private static LoopChainAction Action() => new(LoopChainAction.TaskType, "Define requirements", "requirements",
        agentProvider: LoopChainAction.AutomaticAgent, connectionId: Connection, model: "test-model");

    [Fact]
    public async Task Automatic_agent_returns_structured_output_with_context_and_fresh_session()
    {
        var model = new Gateway(new JsonObject { ["content"] = "{\"requirements\":[\"search\"]}" });
        var runner = Runner(model);
        var result = await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Action(), "{\"scope\":\"real estate\"}", default);
        Assert.Equal("search", result["requirements"]![0]!.GetValue<string>());
        Assert.Equal(2, model.Resets);
        Assert.Contains("Define requirements", model.Messages![0]!["content"]!.GetValue<string>());
        Assert.Contains("real estate", model.Messages[1]!["content"]!.GetValue<string>());
        Assert.DoesNotContain(model.Tools!, tool => tool!["function"]!["name"]!.GetValue<string>() is "ask_user" or "delete_job" or "save_workflow" or "run_workflow");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Done")]
    [InlineData("{\"incomplete\":" )]
    [InlineData("[]")]
    public async Task Invalid_final_output_fails_instead_of_claiming_success(string content)
    {
        var model = new Gateway(new JsonObject { ["content"] = content });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default));
        Assert.Equal(2, model.Resets);
    }

    [Fact]
    public async Task Agent_can_call_authoring_tool_and_receive_its_result()
    {
        var model = new Gateway(Call("job_authoring_guide", "{}"), new JsonObject { ["content"] = "{\"done\":true}" });
        await Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default);
        Assert.Contains(model.Messages!, message => message!["role"]!.GetValue<string>() == "tool" && message["content"]!.GetValue<string>().Contains("runtime"));
    }

    [Fact]
    public async Task Unavailable_tool_is_not_executed_and_is_reported_to_agent()
    {
        var model = new Gateway(Call("delete_job", "{}"), new JsonObject { ["content"] = "{\"blocked\":true}" });
        await Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default);
        Assert.Contains(model.Messages!, message => message!["role"]!.GetValue<string>() == "tool" && message["content"]!.GetValue<string>().Contains("not available"));
    }

    [Fact]
    public async Task Missing_connection_and_permission_prevent_model_execution()
    {
        var model = new Gateway();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model, enabled: false).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Runner(model, permitted: false).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default));
        Assert.Null(model.Messages);
    }

    [Fact]
    public async Task Cancellation_reaches_the_model_and_cleans_up_its_session()
    {
        var model = new Gateway { Block = true };
        using var cancel = new CancellationTokenSource();
        var task = Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, cancel.Token);
        await model.Started.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(2, model.Resets);
    }

    [Fact]
    public async Task Progress_reports_waits_public_updates_tools_and_completion_without_private_reasoning_or_raw_arguments()
    {
        var call = Call("job_authoring_guide", "{\"ignored\":\"private-input\"}");
        call["content"] = "I am checking the job runtime before writing tests.";
        call["reasoning_content"] = "private-reasoning";
        var model = new Gateway(call, new JsonObject { ["content"] = "{\"done\":true}" });
        var events = new List<AgentActivity>();
        await Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default, events.Add);
        Assert.Equal(new[] { "model", "response", "update", "tool_started", "tool_completed", "model", "response", "completed" }, events.Select(e => e.Kind));
        Assert.Equal(new[] { 1, 2 }, events.Where(e => e.Kind == "model").Select(e => e.Turn!.Value));
        Assert.Equal("job_authoring_guide", events.Single(e => e.Kind == "tool_started").Tool);
        Assert.True(events.Single(e => e.Kind == "tool_completed").DurationSeconds >= 0);
        var log = System.Text.Json.JsonSerializer.Serialize(events);
        Assert.DoesNotContain("private-input", log);
        Assert.DoesNotContain("private-reasoning", log);
    }

    [Fact]
    public async Task Tool_errors_and_invalid_final_results_are_visible_in_activity()
    {
        var model = new Gateway(Call("delete_job", "{}"), new JsonObject { ["content"] = "not JSON" });
        var events = new List<AgentActivity>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default, events.Add));
        Assert.Contains(events, e => e.Kind == "tool_failed");
        Assert.Equal("failed", events.Last().Kind);
        Assert.DoesNotContain(events, e => e.Kind == "completed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Job_run_links_come_from_authorized_results_and_do_not_include_raw_output(bool otherProject)
    {
        var project = Guid.NewGuid();
        var run = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), otherProject ? Guid.NewGuid() : project,
            "Failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], null,
            new("code", "private-source", null, null, null, 1, 1, false));
        var model = new Gateway(Call("get_job_run", "{\"runId\":\"" + run.Id + "\"}"), new JsonObject { ["content"] = "{\"done\":true}" });
        var service = new PlaceContextService(new RunDispatcher(project, run), null!, null!, null!);
        var runner = new WorkflowAgentRunner(model, new Connections(true), service, new Permissions(true), null!);
        var events = new List<AgentActivity>();
        await runner.RunAsync(project, Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default, events.Add);
        Assert.Contains(events, e => e.Kind == "tool_failed");
        Assert.DoesNotContain("private-source", System.Text.Json.JsonSerializer.Serialize(events));
        if (otherProject) Assert.DoesNotContain(events, e => e.RunId.HasValue);
        else Assert.Equal(run.Id, Assert.Single(events, e => e.RunId.HasValue).RunId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Job_logs_are_linked_before_execution_finishes_only_after_project_authorization(bool otherProject)
    {
        var project = Guid.NewGuid();
        var job = new JobView(Guid.NewGuid(), otherProject ? Guid.NewGuid() : project, "Checks", null,
            "code", null, "python", "print('ok')", "main.py", [], 1, ["{}"], new Dictionary<string, string>(),
            null, null, null, null, null, [], null, 1, [0], [], false, false, [], [], JobReturnType.Json, null,
            0, 0, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var jobs = new BlockingJobs();
        var service = new PlaceContextService(new JobDispatcher(project, job), jobs, null!, null!);
        var model = new Gateway(Call("run_job", "{\"jobId\":\"" + job.Id + "\",\"inputPayload\":null}"),
            new JsonObject { ["content"] = "{\"done\":true}" });
        var runner = new WorkflowAgentRunner(model, new Connections(true), service, new Permissions(true), null!);
        var events = new List<AgentActivity>();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var task = runner.RunAsync(project, Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, cancel.Token, events.Add);
        if (otherProject)
        {
            await task;
            Assert.False(jobs.Started.Task.IsCompleted);
            Assert.DoesNotContain(events, e => e.RunId.HasValue);
        }
        else
        {
            var id = await jobs.Started.Task.WaitAsync(cancel.Token);
            Assert.False(task.IsCompleted);
            Assert.Equal(id, Assert.Single(events, e => e.Kind == "job_started").RunId);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
    }

    [Fact]
    public async Task Public_model_progress_is_reported_while_the_model_is_still_running()
    {
        var model = new Gateway { Block = true, Progress = "Checking requirements" };
        var events = new List<AgentActivity>();
        using var cancel = new CancellationTokenSource();
        var task = Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, cancel.Token, events.Add);
        await model.Started.Task;
        Assert.False(task.IsCompleted);
        Assert.Contains(events, e => e.Kind == "update" && e.Message == model.Progress);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private sealed class BlockingJobs : IJobRunner
    {
        public TaskCompletionSource<Guid> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JobRunDetailView> RunAsync(Guid jobId, string? inputPayload = null, Guid? runId = null,
            Guid? replayOfRunId = null, CancellationToken ct = default)
        {
            Started.SetResult(runId!.Value);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
    }

    private sealed class JobDispatcher(Guid project, JobView job) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(query switch
        {
            GetProjectByIdQuery => (object)new ProjectSummaryView(project, "test", "/test", "Registered", false, 0, 0, 0),
            GetJobQuery => job,
            _ => throw new NotSupportedException(query.GetType().Name)
        }));
    }

    private sealed class RunDispatcher(Guid project, JobRunDetailView run) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(query switch
        {
            GetProjectByIdQuery => (object)new ProjectSummaryView(project, "test", "/test", "Registered", false, 0, 0, 0),
            GetJobRunQuery => run,
            _ => throw new NotSupportedException(query.GetType().Name)
        }));
    }

    private static JsonObject Call(string name, string args) => new()
    {
        ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = "call-1", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args } })
    };
    private static WorkflowAgentRunner Runner(Gateway model, bool enabled = true, bool permitted = true) =>
        new(model, new Connections(enabled), null!, new Permissions(permitted), null!);
    private sealed class Gateway(params JsonObject[] replies) : IBuildModelGateway
    {
        private readonly Queue<JsonObject> _replies = new(replies);
        public int Resets;
        public bool Block;
        public string? Progress;
        public JsonArray? Messages;
        public JsonArray? Tools;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask ResetSessionAsync() { Resets++; return ValueTask.CompletedTask; }
        public async Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            Messages = (JsonArray)messages.DeepClone(); Tools = tools;
            if (Progress is not null) report?.Invoke(Progress);
            Started.TrySetResult();
            if (Block) await Task.Delay(Timeout.Infinite, ct);
            return _replies.Dequeue();
        }
    }
    private sealed class Connections(bool enabled) : IModelConnections
    {
        public Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelConnection>>([new(Connection, "Test", "openai", "https://example.test", ["test-model"], enabled, true)]);
        public Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
