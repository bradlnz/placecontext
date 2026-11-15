using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

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
        Assert.DoesNotContain("save_job", names); Assert.DoesNotContain("create_schedule", names);
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
        Assert.Equal(BuildRunStatus.Queued, (await fixture.Store.LoadAsync(fixture.ProjectId, other.Id)).Status);
        await vm.DisposePageAsync();
    }

    private sealed class EditorDispatcher(Guid project) : IDispatcher
    {
        private readonly ToolDispatcher _queries = new(project);
        public JobChainView Workflow = new(Guid.NewGuid(), project, "Original", null,
            [new([], Action: new QueryDataChainActionView("SELECT 1", "result"))], DateTimeOffset.UtcNow);
        public int Saves;
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            _queries.Workflow = Workflow;
            return _queries.Query(query, ct);
        }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
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
            Assert.DoesNotContain(tools, tool => tool?["function"]?["name"]?.ToString() is "run_workflow" or "save_job" or "delete_workflow");
            var name = _turn++ == 0 ? "get_workflow" : "save_workflow";
            if (_turn > 2) return Task.FromResult(new JsonObject { ["content"] = "Renamed the workflow." });
            var args = name == "get_workflow" ? JsonSerializer.Serialize(new { workflowId = workflow.Id })
                : JsonSerializer.Serialize(new { workflowId = workflow.Id, name = "Reviewed workflow", description = workflow.Description, stages = workflow.Stages.Select(WorkflowStage.FromView).ToArray() });
            return Task.FromResult(new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject
                { ["id"] = "edit-" + _turn, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args } }) });
        }
    }
}
