using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Workflows;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowMarkupTests
{
    [Fact]
    public void Agent_review_without_a_provider_defaults_to_background_execution()
    {
        var id = Guid.NewGuid();
        var document = WorkflowMarkup.Read($"""
            version: 1
            workflowId: {id}
            name: Review
            stages:
            - jobIds: []
              review:
                type: agentInput
                instructions: Check the evidence
                responseField: review
            """, id);
        var review = Assert.IsType<LoopChainAction>(Assert.Single(document.Stages).ToStage().Action);
        Assert.Equal(LoopChainAction.AutomaticAgent, review.AgentProvider);
    }

    [Fact]
    public void Yaml_round_trip_preserves_actions_settings_conditions_and_nested_branches()
    {
        var vm = Editor(new Store());
        ChainAction[] actions = [
            new VirtualMachineChainAction("browser-linux", "Open https://example.test\nCheck: \"true\"", "browser", 2048, 2, 120, true),
            new LoopChainAction(LoopChainAction.TaskType, "Check the result", "review", agentProvider: LoopChainAction.AutomaticAgent, connectionId: Guid.NewGuid(), model: "test", agentId: Guid.NewGuid()),
            new RepeatFlowChainAction("eq:review.passed:true", 3),
            new QueryDataChainAction("SELECT 1", "rows"),
            new ScoringGateChainAction("review", "score", [new("quality", "Is this correct?", ["No", "Yes"]) ]),
            new ContainerBuildChainAction("FROM alpine\nRUN echo true"), new ContainerRunChainAction(),
            new SubWorkflowChainAction(Guid.NewGuid(), "Worker"),
            new ParallelWorkflowsChainAction([new(Guid.NewGuid(), "First", "eq:ok:true"), new(Guid.NewGuid(), "Second", "exists:ok")]),
            new SendEmailChainAction("test@example.test", "Test", "Subject", "Body"), new SendSmsChainAction("+61400000000", "Body")
        ];
        var job = Guid.NewGuid();
        var stages = actions.Select(action => vm.BuildStageView(new ChainStage([], action: action, executionPolicy: new(2, 5, 60)))).ToList();
        stages.Add(vm.BuildStageView(new([job], new ConditionGate("all:[\"eq:ok:true\",\"gte:score:0.9\"]", [new([job], new WaitGate(TimeSpan.FromSeconds(12)))]))));
        var workflow = new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "true", "00123", stages, DateTimeOffset.UtcNow);
        var yaml = WorkflowMarkup.Write(workflow);
        Assert.Contains("version: 1", yaml); Assert.DoesNotContain("displayName:", yaml);
        var read = WorkflowMarkup.Read(yaml, workflow.Id);
        Assert.Equal("true", read.Name); Assert.Equal("00123", read.Description);
        Assert.Equal(JsonSerializer.Serialize(stages.Select(WorkflowStage.FromView)), JsonSerializer.Serialize(read.Stages));
        var inline = System.Text.RegularExpressions.Regex.Replace(yaml, "description:.*", "description: |\n  A multiline description\n  with a second line");
        Assert.Equal("A multiline description\nwith a second line\n", WorkflowMarkup.Read(inline, workflow.Id).Description);
    }

    [Theory]
    [InlineData("version: 2")]
    [InlineData("name: first\nname: second")]
    [InlineData("unknownField: value")]
    [InlineData("name: &x [*x]")]
    [InlineData("name: !!str hello")]
    [InlineData("name: [unclosed")]
    [InlineData("---\nname: next")]
    public void Invalid_yaml_is_rejected(string extra)
    {
        var workflow = new Store().Workflow;
        var yaml = WorkflowMarkup.Write(workflow);
        Assert.ThrowsAny<Exception>(() => WorkflowMarkup.Read(yaml + "\n" + extra, workflow.Id));
    }

    [Fact]
    public void Wrong_target_missing_fields_and_oversized_input_are_rejected()
    {
        var workflow = new Store().Workflow;
        Assert.Throws<ArgumentException>(() => WorkflowMarkup.Read(WorkflowMarkup.Write(workflow), Guid.NewGuid()));
        Assert.ThrowsAny<Exception>(() => WorkflowMarkup.Read($"version: 1\nworkflowId: {workflow.Id}\nname: Empty\nstages: []", workflow.Id));
        Assert.ThrowsAny<Exception>(() => WorkflowMarkup.Read($"version: 1\nworkflowId: {workflow.Id}\nname: Empty\nstages:\n- query:\n    sql: SELECT 1\n    outputField: result", workflow.Id));
        Assert.Throws<ArgumentException>(() => WorkflowMarkup.Read(new string('a', 262145), workflow.Id));
    }

    [Fact]
    public async Task Valid_yaml_updates_the_draft_immediately_and_only_saves_when_requested()
    {
        var store = new Store(); var vm = Editor(store);
        await vm.OpenChainEditorAsync(store.Workflow, view: "markup");
        Assert.True(vm.IsCanvasView);
        var edited = "# Keep my comment\n" + vm.MarkupText.Replace("Original", "Edited").Replace("SELECT 1", "SELECT 2");
        Assert.True(vm.ApplyMarkup(edited));
        Assert.Equal("Edited", vm.EdName);
        Assert.Equal("SELECT 2", Assert.IsType<QueryDataChainAction>(vm.EdStageActions[0]).Sql);
        Assert.Equal(0, store.Saves);
        vm.SyncMarkupFromCanvas();
        vm.SetEditorView("details"); vm.SetEditorView("canvas");
        Assert.Equal(edited, vm.MarkupText);
        await vm.SaveChainAsync(closeEditor: false);
        Assert.Null(vm.EditorError); Assert.Equal("Edited", store.Workflow.Name);
        Assert.Equal(1, store.Saves);
        store.Workflow = store.Workflow with { UpdatedAt = store.Workflow.UpdatedAt.AddSeconds(1) };
        await vm.SaveChainAsync(closeEditor: false);
        Assert.Contains("newer revision", vm.EditorError); Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Invalid_yaml_preserves_the_last_valid_canvas_and_blocks_saving_until_fixed()
    {
        var store = new Store(); var vm = Editor(store);
        await vm.OpenChainEditorAsync(store.Workflow);
        var initial = vm.MarkupText;
        var stage = vm.EdStages[0];
        Assert.False(vm.ApplyMarkup("name: [invalid"));
        vm.SyncMarkupFromCanvas();
        Assert.Same(stage, vm.EdStages[0]);
        Assert.Equal("name: [invalid", vm.MarkupText);
        Assert.NotNull(vm.MarkupError);
        vm.SetEditorView("details");
        await vm.SaveChainAsync();
        Assert.Equal(0, store.Saves); Assert.NotNull(vm.EditorError);
        vm.SetEditorView("canvas");
        Assert.Equal("name: [invalid", vm.MarkupText);
        Assert.True(vm.ApplyMarkup(initial.Replace("Original", "Corrected")));
        Assert.Null(vm.MarkupError); Assert.Equal("Corrected", vm.EdName);
    }

    [Fact]
    public async Task Canvas_changes_refresh_yaml_without_overwriting_an_invalid_draft()
    {
        var store = new Store(); var vm = Editor(store);
        await vm.OpenChainEditorAsync(store.Workflow);
        vm.EdName = "Canvas edit";
        vm.SyncMarkupFromCanvas();
        Assert.Contains("Canvas edit", vm.MarkupText);
        var valid = vm.MarkupText;
        vm.ApplyMarkup("name: [invalid");
        vm.EdName = "Another canvas edit";
        vm.SyncMarkupFromCanvas();
        Assert.Equal("name: [invalid", vm.MarkupText);
        Assert.False(vm.ApplyMarkup(valid));
        Assert.Contains("canvas changed", vm.MarkupError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Another canvas edit", vm.EdName);
        vm.ReloadMarkup();
        Assert.Null(vm.MarkupError); Assert.Contains("Another canvas edit", vm.MarkupText);
        vm.ApplyMarkup("name: [invalid");
        await vm.OpenChainEditorAsync(store.Workflow);
        Assert.Null(vm.MarkupError); Assert.Contains("Original", vm.MarkupText);
    }

    [Fact]
    public void New_workflows_accept_yaml_before_they_have_a_saved_id()
    {
        var store = new Store(); var vm = Editor(store);
        vm.NewChain("New", [new([], action: new QueryDataChainAction("SELECT 1", "result"))]);
        Assert.True(vm.ApplyMarkup(vm.MarkupText.Replace("SELECT 1", "SELECT 2")));
        Assert.Null(vm.EditChainId); Assert.Equal(0, store.Saves);
        Assert.Equal("SELECT 2", Assert.IsType<QueryDataChainAction>(vm.EdStageActions[0]).Sql);
    }

    [Fact]
    public async Task Assistant_keeps_canvas_mounted_and_refreshes_revision_for_the_next_save()
    {
        var store = new Store(); var vm = Editor(store);
        await vm.OpenChainEditorAsync(store.Workflow);
        var stage = vm.EdStages[0];
        var canvasClosed = false;
        vm.Attach(() => { canvasClosed |= !vm.ShowEditor; return Task.CompletedTask; });
        vm.EdName = "Saved before chat";
        await vm.OpenWorkflowAssistantAsync();
        Assert.Null(vm.EditorError); Assert.True(vm.ShowWorkflowAssistant);
        Assert.Equal("Saved before chat", vm.AssistantWorkflow!.Name);
        Assert.Same(stage, vm.EdStages[0]);
        vm.CloseWorkflowAssistant();
        await vm.OpenWorkflowAssistantAsync();
        Assert.Null(vm.EditorError); Assert.Equal(2, store.Saves);
        Assert.Same(stage, vm.EdStages[0]);
        vm.CloseWorkflowAssistant();
        vm.EdName = "";
        await vm.OpenWorkflowAssistantAsync();
        Assert.NotNull(vm.EditorError); Assert.False(vm.ShowWorkflowAssistant);
        Assert.Equal(2, store.Saves);
        Assert.False(canvasClosed);
    }

    private static JobChainsViewModel Editor(Store store) => new(new PlaceContextService(store, null!, null!, null!), null!, null!, new Navigation());
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) { Uri = ToAbsoluteUri(uri).AbsoluteUri; }
    }
    private sealed class Store : IDispatcher
    {
        public JobChainView Workflow = new(Guid.NewGuid(), Guid.NewGuid(), "Original", null, [new([], Action: new QueryDataChainActionView("SELECT 1", "result"))], DateTimeOffset.UtcNow);
        public int Saves;
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)(query switch
        {
            ListChainRunsQuery => (object)Array.Empty<ChainRunView>(), ListJobChainsQuery => new[] { Workflow }, _ => throw new NotSupportedException()
        }));
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            var update = Assert.IsType<UpdateJobChainCommand>(command);
            Assert.Equal(Workflow.Id, update.ChainId);
            if (update.ExpectedUpdatedAt != Workflow.UpdatedAt) throw new InvalidOperationException("A newer revision exists.");
            Workflow = Workflow with { Name = update.Name, Description = update.Description, UpdatedAt = Workflow.UpdatedAt.AddSeconds(1) };
            Saves++; return Task.FromResult((T)(object)Workflow);
        }
    }
}
