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
        var stages = actions.Select(action => vm.BuildStageView(ChainStage.ForAction(action))).ToList();
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
    public async Task Deploy_updates_workflow_and_canvas_but_keeps_invalid_or_stale_drafts()
    {
        var store = new Store(); var vm = Editor(store);
        await vm.OpenChainEditorAsync(store.Workflow, view: "markup");
        Assert.True(vm.IsMarkupView);
        var initial = vm.MarkupText;
        vm.MarkupText = initial.Replace("Original", "Edited");
        vm.SetEditorView("canvas"); vm.SetEditorView("markup");
        Assert.Contains("Edited", vm.MarkupText);
        await vm.DeployMarkupAsync(vm.MarkupText);
        Assert.Null(vm.EditorError); Assert.Equal("Edited", store.Workflow.Name); Assert.Equal("Edited", vm.EdName);
        Assert.Equal(1, store.Saves); Assert.True(vm.IsMarkupView); Assert.Contains("deployed", vm.MarkupNotice);
        await vm.DeployMarkupAsync("name: [invalid");
        Assert.NotNull(vm.EditorError); Assert.Equal("name: [invalid", vm.MarkupText); Assert.Equal(1, store.Saves);
        vm.ReloadMarkup();
        vm.EdName = "Changed on canvas";
        await vm.DeployMarkupAsync(vm.MarkupText);
        Assert.Contains("canvas changed", vm.EditorError); Assert.Equal(1, store.Saves);
        vm.ReloadMarkup(); store.Workflow = store.Workflow with { UpdatedAt = store.Workflow.UpdatedAt.AddSeconds(1) };
        await vm.DeployMarkupAsync(vm.MarkupText);
        Assert.Contains("newer revision", vm.EditorError); Assert.Equal(1, store.Saves);
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
