using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Application.Access.Models;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Fact]
    public async Task Workflow_editor_cannot_run_delete_create_or_modify_other_workflows()
    {
        var project = Guid.NewGuid();
        var dispatcher = new EditorDispatcher(project);
        var tools = new BuildTools(project, new(dispatcher, null!, null!, null!), new Permissions(), null!,
            workflowEditorId: dispatcher.Workflow.Id, workflowEditorVersion: dispatcher.Workflow.UpdatedAt);
        var names = tools.Functions().Select(tool => tool.Name).ToArray();
        Assert.Contains("save_workflow", names); Assert.Contains("get_workflow", names);
        Assert.DoesNotContain("run_workflow", names); Assert.DoesNotContain("delete_workflow", names);
        Assert.Contains("save_job", names); Assert.Contains("job_authoring_guide", names); Assert.DoesNotContain("create_schedule", names);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.run_workflow(dispatcher.Workflow.Id, null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.delete_workflow(dispatcher.Workflow.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.save_workflow("Other", null, [], Guid.NewGuid(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.save_workflow("New", null, [], null, default));
        Assert.Equal(0, dispatcher.Saves);
    }

    [Fact]
    public async Task Workflow_editor_preserves_its_snapshot_and_stops_on_concurrent_changes()
    {
        var project = Guid.NewGuid(); var dispatcher = new EditorDispatcher(project);
        var tools = new BuildTools(project, new(dispatcher, null!, null!, null!), new Permissions(), null!,
            workflowEditorId: dispatcher.Workflow.Id, workflowEditorVersion: dispatcher.Workflow.UpdatedAt);
        var stages = dispatcher.Workflow.Stages.Select(WorkflowStage.FromView).ToArray();
        await tools.save_workflow("First edit", null, stages, dispatcher.Workflow.Id, default);
        await tools.save_workflow("Second edit", null, stages, dispatcher.Workflow.Id, default);
        Assert.Equal(2, dispatcher.Saves);
        dispatcher.Workflow = dispatcher.Workflow with { UpdatedAt = dispatcher.Workflow.UpdatedAt.AddMinutes(1) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.save_workflow("Stale edit", null, stages, dispatcher.Workflow.Id, default));
        Assert.Equal("Second edit", dispatcher.Workflow.Name);
    }

    [Fact]
    public async Task Workflow_editor_queues_a_separate_scoped_chat_and_saves_through_the_existing_agent()
    {
        await using var fixture = new Fixture(); await fixture.SeedAsync();
        var dispatcher = new EditorDispatcher(fixture.ProjectId);
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var model = new EditorModel(dispatcher.Workflow);
        var agent = new BuildChatAgent(fixture.Store, model, service, fixture.Permissions, fixture.Tenant);
        var other = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test" };
        await agent.QueueAsync(other, "Unrelated task");
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, agent, new(), new NoJs(), service, fixture.Permissions);
        await vm.LoadWorkflowEditorAsync(fixture.ProjectId, dispatcher.Workflow.Id, dispatcher.Workflow.UpdatedAt);
        Assert.NotEqual(other.Id, vm.Conversation.Id); Assert.Empty(vm.Conversation.Messages);
        vm.Draft = "Rename this workflow to Reviewed workflow.";
        await vm.SendAsync(); Assert.Null(vm.Error);
        var queued = await fixture.Store.LoadAsync(fixture.ProjectId, vm.Conversation.Id);
        Assert.Equal(dispatcher.Workflow.Id, queued.WorkflowEditorId);
        await agent.RunAsync(queued, default);
        var completed = await fixture.Store.LoadAsync(fixture.ProjectId, queued.Id);
        Assert.Equal(BuildRunStatus.Completed, completed.Status);
        Assert.Equal("Reviewed workflow", dispatcher.Workflow.Name); Assert.Equal(1, dispatcher.Saves);
        Assert.Equal(dispatcher.Workflow.UpdatedAt, completed.WorkflowEditorVersion);
        Assert.Contains("dedicated workflow editor", completed.SystemPrompt);
        Assert.Contains("edit code and map/reduce runtimes", completed.SystemPrompt);
        Assert.Equal(BuildRunStatus.Queued, (await fixture.Store.LoadAsync(fixture.ProjectId, other.Id)).Status);
        await vm.DisposePageAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_editor_can_convert_attached_job_runtimes_including_false_branches(bool falseBranch)
    {
        var project = Guid.NewGuid(); var dispatcher = new EditorDispatcher(project);
        var job = dispatcher.Job;
        var jobStage = new JobChainStageView([new(job.Id, job.Name)]);
        dispatcher.Workflow = dispatcher.Workflow with { Stages = falseBranch
            ? [new([], new ConditionGateView("false"), [jobStage], new QueryDataChainActionView("SELECT 1", "result"))] : [jobStage] };
        var tools = new BuildTools(project, new(dispatcher, null!, null!, null!), new Permissions(), null!,
            workflowEditorId: dispatcher.Workflow.Id, workflowEditorVersion: dispatcher.Workflow.UpdatedAt);
        var definition = JsonSerializer.Deserialize<JobRequest>(JsonSerializer.Serialize(await tools.get_job(job.Id, default)))!;
        definition = definition with { MapRuntimeId = "node", MapSource = null, MapEntrypoint = "index.js", MapFiles = [new("index.js", "console.log(JSON.stringify({valid:true}));")] };
        await tools.save_job(definition, job.Id, default);
        var saved = Assert.IsType<UpdateJobCommand>(dispatcher.SavedJob);
        Assert.Equal("node", saved.MapRuntimeId); Assert.Equal("index.js", saved.MapEntrypoint);
        Assert.Equal(definition.MapFiles[0].Content, Assert.Single(saved.MapFiles!).Content);
        Assert.Equal(job.InputPayloads, saved.InputPayloads); Assert.Equal(job.Parameters, saved.Parameters);
        Assert.Equal(job.MapEnv, saved.MapEnv); Assert.Equal(job.ReturnType, saved.ReturnType);
        Assert.Equal(job.SuccessExitCodes, saved.SuccessExitCodes); Assert.Equal(job.RetryCount, saved.RetryCount);
        Assert.Equal(job.ConcurrencyLimit, saved.ConcurrencyLimit);
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("create")]
    [InlineData("permission")]
    [InlineData("project")]
    [InlineData("stale")]
    public async Task Workflow_editor_job_updates_retain_scope_and_permission_checks(string failure)
    {
        var project = Guid.NewGuid(); var dispatcher = new EditorDispatcher(project);
        dispatcher.Workflow = dispatcher.Workflow with { Stages = [new([new(dispatcher.Job.Id, dispatcher.Job.Name)])] };
        var permissions = new Permissions { Denied = failure == "permission" ? Permission.JobsEdit : null };
        var tools = new BuildTools(project, new(dispatcher, null!, null!, null!), permissions, null!,
            workflowEditorId: dispatcher.Workflow.Id, workflowEditorVersion: dispatcher.Workflow.UpdatedAt);
        var definition = JsonSerializer.Deserialize<JobRequest>(JsonSerializer.Serialize(JobDefinitionMapper.ToResponse(dispatcher.Job)))!;
        if (failure == "project") dispatcher.Job = dispatcher.Job with { ProjectId = Guid.NewGuid() };
        if (failure == "stale") dispatcher.Workflow = dispatcher.Workflow with { UpdatedAt = dispatcher.Workflow.UpdatedAt.AddMinutes(1) };
        Guid? jobId = failure == "create" ? null : failure == "unrelated" ? Guid.NewGuid() : dispatcher.Job.Id;
        if (failure is "project" or "stale")
            await Assert.ThrowsAsync<InvalidOperationException>(() => tools.save_job(definition, jobId, default));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.save_job(definition, jobId, default));
        Assert.Null(dispatcher.SavedJob);
    }

    private sealed class EditorDispatcher(Guid project) : IDispatcher
    {
        private readonly ToolDispatcher _queries = new(project);
        public JobView Job = new(
            Id: Guid.NewGuid(), ProjectId: project, Name: "Validate", Description: "Keep the input contract",
            MapSourceKind: "code", MapImage: null, MapRuntimeId: "python", MapSource: "print('test')", MapEntrypoint: "main.py",
            MapFiles: [new("main.py", "print('test')")], ShardCount: 1, InputPayloads: ["{\"valid\":true}"], MapEnv: new Dictionary<string, string> { ["MODE"] = "validate" },
            ReduceSourceKind: null, ReduceImage: null, ReduceRuntimeId: null, ReduceSource: null,
            ReduceEntrypoint: null, ReduceFiles: [], ReduceEnv: null, ConcurrencyLimit: 2,
            SuccessExitCodes: [0], PartialExitCodes: [2], AllowNetworkEgress: false, AllowApiInvocation: true,
            Parameters: [new("valid", Type: "checkbox")], PostJobActions: [], ReturnType: JobReturnType.Json, ReturnFileName: null,
            RetryCount: 2, RetryDelaySeconds: 5, McpConnectionIds: [], CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        public UpdateJobCommand? SavedJob;
        public JobChainView Workflow = new(Guid.NewGuid(), project, "Original", null,
            [new([], Action: new QueryDataChainActionView("SELECT 1", "result"))], DateTimeOffset.UtcNow);
        public int Saves;
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            if (query is GetJobQuery) return Task.FromResult((T)(object)Job);
            _queries.Workflow = Workflow;
            return _queries.Query(query, ct);
        }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            if (command is UpdateJobCommand jobUpdate)
            {
                SavedJob = jobUpdate;
                return Task.FromResult((T)(object)Job);
            }
            var update = Assert.IsType<UpdateJobChainCommand>(command);
            Assert.Equal(Workflow.Id, update.ChainId);
            if (update.ExpectedUpdatedAt != Workflow.UpdatedAt) throw new InvalidOperationException("Workflow changed; reopen the editor.");
            Workflow = Workflow with { Name = update.Name!, Description = update.Description, UpdatedAt = Workflow.UpdatedAt.AddSeconds(1) };
            Saves++;
            return Task.FromResult((T)(object)Workflow);
        }
    }

    private sealed class EditorModel(JobChainView workflow) : IBuildModelGateway
    {
        private int _turn;
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            Assert.Contains("dedicated workflow editor", messages[0]!["content"]!.ToString());
            Assert.DoesNotContain(tools, tool => tool?["function"]?["name"]?.ToString() is "run_workflow" or "delete_job" or "delete_workflow");
            var name = _turn++ == 0 ? "get_workflow" : "save_workflow";
            if (_turn > 2) return Task.FromResult(new JsonObject { ["content"] = "Renamed the workflow." });
            var args = name == "get_workflow" ? JsonSerializer.Serialize(new { workflowId = workflow.Id })
                : JsonSerializer.Serialize(new { workflowId = workflow.Id, name = "Reviewed workflow", description = workflow.Description, stages = workflow.Stages.Select(WorkflowStage.FromView).ToArray() });
            return Task.FromResult(new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject
                { ["id"] = "edit-" + _turn, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args } }) });
        }
    }
}
