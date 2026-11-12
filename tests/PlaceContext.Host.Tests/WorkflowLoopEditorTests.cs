using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public class WorkflowLoopEditorTests
{
    [Fact]
    public async Task Query_data_editor_round_trips_into_following_scoring_input()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation()); vm.NewChain();
        vm.OpenQueryDataAction(0); vm.SaveQueryDataAction(); Assert.NotNull(vm.QueryDataError); Assert.Empty(vm.EdStages);
        vm.QueryDataSql = "SELECT id FROM listings LIMIT 10"; vm.QueryDataOutputField = "listings"; vm.SaveQueryDataAction(); Assert.Null(vm.QueryDataError);
        var stages = vm.BuildCanvasStageViews();
        var action = Assert.IsType<QueryDataChainAction>(PlaceContext.Application.Build.WorkflowStage.FromView(stages[0]).ToStage().Action);
        Assert.Equal("listings", action.OutputField);
        var reopened = new JobChainsViewModel(null!, null!, null!, new Navigation());
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Read", null, stages, DateTimeOffset.UtcNow));
        reopened.OpenQueryDataAction(0, true); Assert.Equal(vm.QueryDataSql, reopened.QueryDataSql);
        reopened.OpenScoringAction(1); Assert.Contains("listings", reopened.ScoreEvidenceFields);
    }

    [Fact]
    public async Task Scoring_editor_round_trips_and_maps_previous_action_fields()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation()); vm.NewChain();
        await vm.OpenLoopActionAt(LoopChainAction.AgentType, 0); vm.LoopAgentProvider = LoopChainAction.ExternalAgent;
        vm.LoopInstructions = "Review"; vm.LoopResponseField = "review"; vm.SaveLoopAction();
        vm.OpenScoringAction(1); Assert.Contains("review", vm.ScoreEvidenceFields); Assert.Equal("review", vm.ScoreInputField);
        vm.ScorePass = 95; vm.ScoreConfidence = .9; vm.ScoreStopOnFailure = false; vm.SaveScoringAction(); Assert.Null(vm.ScoreError);
        var stages = vm.BuildCanvasStageViews();
        var action = Assert.IsType<ScoringGateChainAction>(PlaceContext.Application.Build.WorkflowStage.FromView(stages[1]).ToStage().Action);
        Assert.Equal(95, action.PassScore); Assert.False(action.StopOnFailure);
        var reopened = new JobChainsViewModel(null!, null!, null!, new Navigation());
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Score", null, stages, DateTimeOffset.UtcNow));
        reopened.OpenScoringAction(1, true); Assert.Equal(.9, reopened.ScoreConfidence); Assert.Single(reopened.ScoreCriteria);
    }

    [Fact]
    public async Task Agent_task_and_repeat_survive_editor_and_tool_round_trips()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation());
        vm.NewChain();
        await vm.OpenLoopActionAt(LoopChainAction.TaskType, 0);
        vm.LoopAgentProvider = LoopChainAction.ExternalAgent;
        vm.LoopInstructions = "Define the requirements";
        vm.SaveLoopAction();
        vm.OpenRepeatAction();
        vm.SaveRepeatAction();
        Assert.Null(vm.RepeatError);
        var stages = vm.BuildCanvasStageViews();
        var repeat = PlaceContext.Application.Build.WorkflowStage.FromView(stages[1]).ToStage();
        Assert.Equal(3, Assert.IsType<RepeatFlowChainAction>(repeat.Action).MaxAttempts);
        var reopened = new JobChainsViewModel(null!, null!, null!, new Navigation());
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Build", null, stages, DateTimeOffset.UtcNow));
        await reopened.EditLoopAction(0);
        Assert.Equal(LoopChainAction.TaskType, reopened.LoopKind);
        reopened.OpenRepeatAction(1);
        Assert.Equal("eq:readiness.production_ready:true", reopened.RepeatCondition);
    }

    [Fact]
    public async Task Jev_selection_survives_editor_and_build_tool_round_trips()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation());
        vm.NewChain();
        await vm.OpenLoopActionAt(LoopChainAction.AgentType, 0);
        vm.LoopAgentProvider = LoopChainAction.JevAgent;
        vm.LoopInstructions = "Does the reported total match the line items?";
        vm.SaveLoopAction();
        Assert.Null(vm.LoopError);
        var stages = vm.BuildCanvasStageViews();
        var toolStage = PlaceContext.Application.Build.WorkflowStage.FromView(Assert.Single(stages));
        Assert.Equal(LoopChainAction.JevAgent, Assert.IsType<LoopChainAction>(toolStage.ToStage().Action).AgentProvider);
        var reopened = new JobChainsViewModel(null!, null!, null!, new Navigation());
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Verify", null, stages, DateTimeOffset.UtcNow));
        await reopened.EditLoopAction(0);
        Assert.Equal(LoopChainAction.JevAgent, reopened.LoopAgentProvider);
        reopened.SaveLoopAction();
        Assert.Equal(LoopChainAction.JevAgent, Assert.IsType<LoopChainAction>(reopened.EdStageActions[0]).AgentProvider);
        await reopened.OpenLoopActionAt(LoopChainAction.HumanType, 1);
        Assert.Equal(LoopChainAction.ExternalAgent, reopened.LoopAgentProvider);
    }

    [Fact]
    public async Task Qemu_step_settings_survive_editing_and_reopening()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation());
        vm.NewChain();
        vm.OpenQemuActionAt(0);
        vm.SaveQemuAction();
        Assert.NotNull(vm.QemuError);
        Assert.Empty(vm.EdStages);
        vm.VirtualMachineImageProfile = "browser";
        vm.QemuCommand = "Open the browser and report its page title.";
        vm.QemuMemoryMb = 2048;
        vm.QemuAllowNetwork = true;
        vm.SaveQemuAction();
        var reopened = new JobChainsViewModel(null!, null!, null!, new Navigation());
        reopened.EditorTab = "runs";
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "VM task", null, vm.BuildCanvasStageViews(), DateTimeOffset.UtcNow));
        Assert.True(reopened.IsDetailsTab);
        reopened.EditQemuAction(0);
        Assert.Equal("browser", reopened.VirtualMachineImageProfile);
        Assert.Equal("Open the browser and report its page title.", reopened.QemuCommand);
        Assert.Equal(2048, reopened.QemuMemoryMb);
        Assert.True(reopened.QemuAllowNetwork);
        reopened.SaveQemuAction();
        Assert.Single(reopened.EdStages);
    }

    [Fact]
    public async Task Inserting_editing_moving_and_reopening_preserves_step_settings()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, new Navigation());
        vm.NewChain();
        var jobId = Guid.NewGuid();
        vm.AddStage(jobId);
        vm.EdStageGates[0] = new WaitGate(TimeSpan.FromSeconds(10));
        await vm.OpenLoopActionAt(LoopChainAction.HumanType, 0);
        vm.SaveLoopAction();
        Assert.NotNull(vm.LoopError);
        Assert.Single(vm.EdStages);

        vm.LoopInstructions = "Review the draft";
        vm.SaveLoopAction();
        Assert.Equal(2, vm.EdStages.Count);
        Assert.Equal(jobId, Assert.Single(vm.EdStages[1]));
        Assert.IsType<WaitGate>(vm.EdStageGates[1]);
        await vm.EditLoopAction(0);
        vm.LoopResponseField = "review";
        vm.LoopResponseType = LoopChainAction.YesNoResponse;
        vm.SaveLoopAction();
        Assert.Equal(2, vm.EdStages.Count);
        vm.MoveStage(0, 1);

        var stages = vm.BuildCanvasStageViews();
        var reopened = new JobChainsViewModel(null!, null!, null!, new Navigation());
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Review", null, stages, DateTimeOffset.UtcNow));
        var action = Assert.IsType<LoopChainAction>(reopened.EdStageActions[1]);
        Assert.Equal(LoopChainAction.HumanType, action.Type);
        Assert.Equal("Review the draft", action.Instructions);
        Assert.Equal("review", action.ResponseField);
        Assert.Equal(LoopChainAction.YesNoResponse, action.ResponseType);
        Assert.IsType<WaitGate>(reopened.EdStageGates[0]);
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }
}
