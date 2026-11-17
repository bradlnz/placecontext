using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Fact]
    public async Task Workflow_management_side_panel_returns_question_choices_and_refreshes_the_list()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var agent = new BuildChatAgent(fixture.Store, new ManagementQuestionModel(), fixture.Service, fixture.Permissions, fixture.Tenant);
        using var vm = new ContextAssistantViewModel(fixture.Connections, null!, buildAgent: agent, buildStore: fixture.Store);
        await vm.LoadAsync();
        var refreshes = 0;
        vm.Draft = "Help me create a workflow";
        var send = vm.SendAsync("workflow-manager", () => Task.FromResult("Workflow list"), fixture.ProjectId,
            () => { refreshes++; return Task.CompletedTask; });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IReadOnlyList<BuildConversationSummary> sessions;
        while ((sessions = await fixture.Store.ListAsync(fixture.ProjectId, timeout.Token)).Count == 0)
            await Task.Delay(10, timeout.Token);
        var queued = await fixture.Store.LoadAsync(fixture.ProjectId, Assert.Single(sessions).Id);
        Assert.True(queued.WorkflowManagement);
        await agent.RunAsync(queued, timeout.Token);
        await send.WaitAsync(timeout.Token);
        Assert.Null(vm.Error);
        Assert.False(vm.Busy);
        Assert.Equal(1, refreshes);
        Assert.Equal("What should the workflow do?", vm.Messages.Last().Text);
        Assert.Equal(new[] { "Analyze data", "Generate a report" }, vm.Messages.Last().Answers);
        Assert.Null(vm.Suggestion);
    }

    [Fact]
    public async Task Workflow_management_restricts_tools_permissions_and_project_scope()
    {
        var project = Guid.NewGuid();
        var dispatcher = new EditorDispatcher(project);
        var permissions = new Permissions();
        var tools = new BuildTools(project, new(dispatcher, null!, null!, null!), permissions, null!, workflowManagement: true);
        var names = tools.Functions().Select(tool => tool.Name).Order().ToArray();
        Assert.Equal(new[] { "ask_user", "check_workflow_readiness", "delete_workflow", "get_job", "get_workflow", "list_jobs", "list_workflows", "project_context", "save_workflow" }, names);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.run_workflow(dispatcher.Workflow.Id, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.delete_workflow(Guid.NewGuid(), default));
        permissions.Denied = Permission.ChainsManage;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.delete_workflow(dispatcher.Workflow.Id, default));
        Assert.Equal(0, dispatcher.Saves);
    }

    [Fact]
    public async Task Workflow_management_requires_a_current_definition_before_replacing_it()
    {
        var project = Guid.NewGuid();
        var dispatcher = new EditorDispatcher(project);
        var tools = new BuildTools(project, new(dispatcher, null!, null!, null!), new Permissions(), null!, workflowManagement: true);
        var stages = dispatcher.Workflow.Stages.Select(WorkflowStage.FromView).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.save_workflow("Unread", null, stages, dispatcher.Workflow.Id, default));
        await tools.get_workflow(dispatcher.Workflow.Id, default);
        dispatcher.Workflow = dispatcher.Workflow with { UpdatedAt = dispatcher.Workflow.UpdatedAt.AddSeconds(1) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.save_workflow("Stale", null, stages, dispatcher.Workflow.Id, default));
        await tools.list_workflows(default);
        await tools.save_workflow("Updated", null, stages, dispatcher.Workflow.Id, default);
        Assert.Equal("Updated", dispatcher.Workflow.Name);
        Assert.Equal(1, dispatcher.Saves);
    }

    [Fact]
    public async Task Workflow_management_persists_its_role_and_performs_crud_through_the_existing_agent()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var dispatcher = new ManagementDispatcher(fixture.ProjectId);
        var model = new ManagementModel(dispatcher);
        var agent = new BuildChatAgent(fixture.Store, model, new(dispatcher, null!, null!, null!), fixture.Permissions, fixture.Tenant);
        var conversation = new BuildConversation { ProjectId = fixture.ProjectId, ConnectionId = fixture.ConnectionId, Model = "test", WorkflowManagement = true };
        await agent.QueueAsync(conversation, "Create a test workflow, rename it, then delete that test workflow.");
        var queued = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.True(queued.WorkflowManagement);
        await agent.RunAsync(queued, default);
        var completed = await fixture.Store.LoadAsync(fixture.ProjectId, conversation.Id);
        Assert.Null(completed.Error);
        Assert.Equal(BuildRunStatus.Completed, completed.Status);
        Assert.Equal(new[] { "create", "update", "delete" }, dispatcher.Changes);
        Assert.Null(dispatcher.Workflow);
        Assert.Contains("dedicated workflow manager", completed.SystemPrompt);
    }

    private sealed class ManagementDispatcher(Guid project) : IDispatcher
    {
        private readonly ToolDispatcher _queries = new(project);
        public JobChainView? Workflow;
        public List<string> Changes = [];
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            _queries.Workflow = Workflow;
            return _queries.Query(query, ct);
        }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            object result;
            switch (command)
            {
                case CreateJobChainCommand create:
                    Assert.Equal(project, create.ProjectId);
                    Assert.IsType<PlaceContext.Domain.ValueObjects.QueryDataChainAction>(Assert.Single(create.StageActions!));
                    Workflow = new(Guid.NewGuid(), project, create.Name, create.Description,
                        [new([], Action: new QueryDataChainActionView("SELECT 1", "result"))], DateTimeOffset.UtcNow);
                    Changes.Add("create"); result = Workflow; break;
                case UpdateJobChainCommand update:
                    Assert.NotNull(Workflow);
                    Assert.Equal(Workflow.Id, update.ChainId);
                    Assert.Equal(Workflow.UpdatedAt, update.ExpectedUpdatedAt);
                    Workflow = Workflow with { Name = update.Name!, UpdatedAt = Workflow.UpdatedAt.AddSeconds(1) };
                    Changes.Add("update"); result = Workflow; break;
                case DeleteJobChainCommand delete:
                    Assert.NotNull(Workflow);
                    Assert.Equal(Workflow.Id, delete.ChainId);
                    Workflow = null; Changes.Add("delete"); result = true; break;
                default: throw new InvalidOperationException("Unexpected management command.");
            }
            return Task.FromResult((T)result);
        }
    }

    private sealed class ManagementModel(ManagementDispatcher dispatcher) : IBuildModelGateway
    {
        private int _step;
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null)
        {
            Assert.Contains("dedicated workflow manager", messages[0]!["content"]!.ToString());
            Assert.DoesNotContain(tools, tool => tool?["function"]?["name"]?.ToString() is "run_workflow" or "save_job");
            var step = _step++;
            if (step == 4) return Task.FromResult(new JsonObject { ["content"] = "Created, renamed and deleted the test workflow." });
            var name = step is 0 or 2 ? "save_workflow" : step == 1 ? "get_workflow" : "delete_workflow";
            var args = step is 0 or 2 ? JsonSerializer.Serialize(new
                {
                    workflowId = step == 0 ? (Guid?)null : dispatcher.Workflow!.Id,
                    name = step == 0 ? "Test" : "Renamed test", description = (string?)null,
                    stages = new[] { WorkflowStage.FromView(new([], Action: new QueryDataChainActionView("SELECT 1", "result"))) }
                }) : JsonSerializer.Serialize(new { workflowId = dispatcher.Workflow!.Id });
            return Task.FromResult(new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject
                { ["id"] = "manage-" + step, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args } }) });
        }
    }

    private sealed class ManagementQuestionModel : IBuildModelGateway
    {
        public Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct, Action<string>? report = null) =>
            Task.FromResult(new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "question", ["type"] = "function", ["function"] = new JsonObject
                {
                    ["name"] = "ask_user", ["arguments"] = JsonSerializer.Serialize(new
                        { question = "What should the workflow do?", answers = new[] { "Analyze data", "Generate a report" } })
                }
            }) });
    }
}
