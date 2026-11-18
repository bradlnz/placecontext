using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using System.Text.Json.Nodes;
using PlaceContext.Application.Build;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Application.Access;

namespace PlaceContext.Application.Tests;

public sealed class WorkflowAgentRunnerTests
{
    [Fact]
    public async Task Selected_project_agent_applies_access_and_configured_model()
    {
        var agent = new DashboardAgentProfile { Name = "Reviewer", Instructions = "Be precise.", WorkflowAccess = "read", DataAccess = "manage",
            TableAccess = "none", SourceAccess = "none", SourceCapabilities = ["metadata"], ArtifactAccess = "none",
            ConnectionId = Connection, Model = "test-model", WorkflowIds = [Guid.NewGuid()] };
        var store = new Agents(new() { Agents = [agent] });
        var model = new Gateway(Call("run_job", "{}"), new JsonObject { ["content"] = "{\"verified\":true}" });
        var runner = new WorkflowAgentRunner(model, new Connections(true), null!, new Permissions(true), null!, projectAgents: store);
        var action = new LoopChainAction(LoopChainAction.TaskType, "Review the result", "review", agentProvider: LoopChainAction.AutomaticAgent,
            connectionId: Guid.NewGuid(), model: "old-model", agentId: agent.Id);
        await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, action, null, default);
        Assert.Contains("Be precise.", model.Messages![0]!["content"]!.ToString());
        Assert.Contains(agent.WorkflowIds[0].ToString(), model.Messages[0]!["content"]!.ToString());
        Assert.Contains(model.Tools!, t => t!["function"]!["name"]!.ToString() == "get_job");
        Assert.Contains(model.Tools!, t => t!["function"]!["name"]!.ToString() == "list_data_sources");
        Assert.DoesNotContain(model.Tools!, t => t!["function"]!["name"]!.ToString() == "search_data_sources");
        Assert.Contains("\"SourceCapabilities\":[\"metadata\"]", model.Messages[0]!["content"]!.ToString());
        Assert.DoesNotContain(model.Tools!, t => t!["function"]!["name"]!.ToString() is "run_job" or "list_project_tables" or "call_mcp_tool");
        Assert.Contains(model.Messages, m => m!["role"]!.ToString() == "tool" && m["content"]!.ToString().Contains("not available"));
        store.Catalog.Agents.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, action, null, default));
    }

    [Theory]
    [InlineData(LoopChainAction.TaskType)]
    [InlineData(LoopChainAction.AgentType)]
    public async Task Parent_approves_tasks_and_handles_workflow_gates(string kind)
    {
        var parent = new DashboardAgentProfile { Name = "Supervisor", Instructions = "Require evidence", ConnectionId = Connection, Model = "test-model" };
        var child = new DashboardAgentProfile { Name = "Worker", ParentAgentId = parent.Id };
        var model = new Gateway(new JsonObject { ["content"] = "{\"done\":true}" });
        var runner = new WorkflowAgentRunner(model, new Connections(true), null!, new Permissions(true), null!,
            projectAgents: new Agents(new() { Agents = [parent, child] }));
        var action = new LoopChainAction(kind, "Check the evidence", "review", agentProvider: LoopChainAction.AutomaticAgent,
            connectionId: kind == LoopChainAction.TaskType ? Connection : null,
            model: kind == LoopChainAction.TaskType ? "test-model" : null, agentId: child.Id);
        var result = await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, action, "{\"evidence\":42}", default);
        Assert.Equal(kind == LoopChainAction.TaskType ? 2 : 1, model.Approvals.Count);
        Assert.All(model.Approvals, approval =>
        {
            Assert.Equal(Connection, approval.Connection);
            Assert.Contains("Supervisor", approval.Prompt);
            Assert.Contains("Require evidence", approval.Prompt);
            Assert.Contains("42", approval.Evidence);
            Assert.Contains("Do not reject only because completed findings", approval.Prompt);
        });
        if (kind == LoopChainAction.TaskType)
        {
            Assert.Contains("Authorize sub-agent task", model.Approvals[0].Prompt);
            Assert.DoesNotContain("Proposed result", model.Approvals[0].Evidence);
            Assert.Contains("Approve sub-agent result", model.Approvals[1].Prompt);
            Assert.Contains("Proposed result", model.Approvals[1].Evidence);
        }
        if (kind == LoopChainAction.AgentType)
        {
            Assert.Null(model.Messages);
            Assert.True(result["approved"]!.GetValue<bool>());
        }
        else Assert.True(result["done"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Default_review_without_a_configured_model_fails_with_setup_guidance()
    {
        var model = new Gateway();
        var action = new LoopChainAction(LoopChainAction.AgentType, "Check the evidence", "review");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model)
            .RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, action, "{}", default));
        Assert.Contains("Configure the reviewing agent's model", error.Message);
        Assert.Empty(model.Approvals);
    }

    [Theory]
    [InlineData("{\"approved\":false,\"reason\":\"Missing evidence\"}")]
    [InlineData("{\"approved\":\"true\",\"reason\":\"ok\"}")]
    [InlineData("{\"approved\":true}")]
    [InlineData("not JSON")]
    public async Task Denied_or_invalid_parent_approval_stops_the_child(string decision)
    {
        var child = new DashboardAgentProfile { Name = "Worker" };
        var model = new Gateway { ApprovalReply = decision };
        var runner = new WorkflowAgentRunner(model, new Connections(true), null!, new Permissions(true), null!,
            projectAgents: new Agents(new() { Agents = [child] }));
        var action = new LoopChainAction(LoopChainAction.TaskType, "Work", "result", agentProvider: LoopChainAction.AutomaticAgent,
            connectionId: Connection, model: "test-model", agentId: child.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, action, null, default));
        Assert.Null(model.Messages);
        Assert.Single(model.Approvals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Parent_rejection_blocks_tool_execution_and_final_results(bool tool)
    {
        var child = new DashboardAgentProfile { Name = "Worker" };
        var model = new Gateway(tool ? Call("job_authoring_guide", "{}") : new JsonObject { ["content"] = "{\"done\":true}" });
        model.ApprovalReplies.Enqueue("{\"approved\":true,\"reason\":\"Task authorized\"}");
        model.ApprovalReplies.Enqueue("{\"approved\":false,\"reason\":\"Outside task scope\"}");
        var runner = new WorkflowAgentRunner(model, new Connections(true), null!, new Permissions(true), null!,
            projectAgents: new Agents(new() { Agents = [child] }));
        var action = new LoopChainAction(LoopChainAction.TaskType, "Work", "result", agentProvider: LoopChainAction.AutomaticAgent,
            connectionId: Connection, model: "test-model", agentId: child.Id);
        var events = new List<AgentActivity>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, action, null, default, events.Add));
        Assert.Contains("Outside task scope", error.Message);
        Assert.DoesNotContain(events, e => e.Kind is "tool_started" or "completed");
        Assert.Equal(2, model.Approvals.Count);
    }

    [Fact]
    public void Hierarchy_rejects_cycles_missing_parents_and_parent_deletion()
    {
        var parent = new DashboardAgentProfile { Name = "Parent" };
        var child = new DashboardAgentProfile { Name = "Child", ParentAgentId = parent.Id };
        var catalog = new ProjectAgentCatalog { Agents = [parent, child] };
        catalog.Validate();
        parent.ParentAgentId = child.Id;
        Assert.Throws<ArgumentException>(catalog.Validate);
        parent.ParentAgentId = null;
        catalog.Agents.Remove(parent);
        Assert.Throws<ArgumentException>(catalog.Validate);
        child.ParentAgentId = child.Id;
        Assert.Throws<ArgumentException>(catalog.Validate);
        child.ParentAgentId = null;
        catalog.Validate();
        Assert.Equal(DashboardSettings.DefaultAgentId, child.EffectiveParentAgentId);
    }

    private sealed class Agents(ProjectAgentCatalog catalog) : IProjectAgentStore
    {
        public ProjectAgentCatalog Catalog => catalog;
        public Task<ProjectAgentCatalog> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(catalog);
        public Task SaveAsync(Guid id, ProjectAgentCatalog value, CancellationToken ct = default) => throw new NotSupportedException();
    }
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
        var model = new Gateway(Enumerable.Range(0, 3).Select(_ => new JsonObject { ["content"] = content }).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default));
        Assert.Equal(2, model.Resets);
    }

    [Fact]
    public async Task Invalid_output_is_repaired_without_repeating_tools_or_losing_blockers()
    {
        var model = new Gateway(Call("job_authoring_guide", "{}"),
            new JsonObject { ["content"] = "{\"summary\":\"Scope missing\"," },
            new JsonObject { ["content"] = "{\"summary\":\"Scope missing\",\"blockers\":[\"Define budgeting workflows\"],\"questions\":[\"Who will use it?\"]}" });
        var events = new List<AgentActivity>();
        var result = await Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default, events.Add);
        Assert.Equal("Define budgeting workflows", result["blockers"]![0]!.ToString());
        Assert.Empty(model.Tools!);
        Assert.Single(events, e => e.Kind == "tool_started");
        Assert.Single(events, e => e.Kind == "warning");
        Assert.Equal("completed", events.Last().Kind);
        Assert.Contains(model.Messages!, m => m!["role"]!.ToString() == "tool");
        Assert.Contains("Do not continue the task", model.Messages!.Last()!["content"]!.ToString());
    }

    [Fact]
    public async Task Format_repair_is_bounded_and_reports_how_to_recover()
    {
        var model = new Gateway(Enumerable.Range(0, 3).Select(_ => new JsonObject { ["content"] = "incomplete" }).ToArray());
        var events = new List<AgentActivity>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default, events.Add));
        Assert.Contains("two format-repair attempts", error.Message);
        Assert.Contains("Completed job actions remain applied", error.Message);
        Assert.Equal(3, events.Count(e => e.Kind == "model"));
        Assert.DoesNotContain(events, e => e.Kind == "completed");
    }

    [Fact]
    public async Task Tool_requests_during_format_repair_are_never_executed()
    {
        var model = new Gateway(new JsonObject { ["content"] = "incomplete" }, Call("job_authoring_guide", "{}"));
        var events = new List<AgentActivity>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default, events.Add));
        Assert.Contains("No additional actions were run", error.Message);
        Assert.DoesNotContain(events, e => e.Kind is "tool_started" or "completed");
    }

    [Fact]
    public async Task Complete_json_in_a_markdown_fence_is_accepted()
    {
        var model = new Gateway(new JsonObject { ["content"] = "```json\n{\"summary\":\"Scope missing\",\"blockers\":[\"Define scope\"]}\n```" });
        var result = await Runner(model).RunAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Action(), null, default);
        Assert.Equal("Scope missing", result["summary"]!.ToString());
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
        var model = new Gateway(Call("delete_job", "{}"), new JsonObject { ["content"] = "not JSON" }, new JsonObject { ["content"] = "not JSON" }, new JsonObject { ["content"] = "not JSON" });
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
        public Queue<string> ApprovalReplies = new();
        public string ApprovalReply = "{\"approved\":true,\"reason\":\"Within the assigned task\"}";
        public List<(Guid Connection, string Prompt, string Evidence)> Approvals = [];
        public bool Block;
        public string? Progress;
        public JsonArray? Messages;
        public JsonArray? Tools;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask ResetSessionAsync() { Resets++; return ValueTask.CompletedTask; }
        public async Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            if (messages[0]!["content"]!.ToString().StartsWith("You are the parent agent responsible"))
            {
                Approvals.Add((connectionId, messages[0]!["content"]!.ToString(), messages[1]!["content"]!.ToString()));
                return new JsonObject { ["content"] = ApprovalReplies.Count > 0 ? ApprovalReplies.Dequeue() : ApprovalReply };
            }
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
