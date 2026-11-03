using PlaceContext.Application.Dtos;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public class WorkflowLoopEditorTests
{
    [Fact]
    public async Task Jev_selection_survives_editor_and_build_tool_round_trips()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, null!, null!);
        vm.NewChain();
        vm.OpenLoopActionAt(LoopChainAction.AgentType, 0);
        vm.LoopAgentProvider = LoopChainAction.JevAgent;
        vm.LoopInstructions = "Does the reported total match the line items?";
        vm.SaveLoopAction();
        Assert.Null(vm.LoopError);
        var stages = vm.BuildCanvasStageViews();
        var toolStage = PlaceContext.Application.Build.WorkflowStage.FromView(Assert.Single(stages));
        Assert.Equal(LoopChainAction.JevAgent, Assert.IsType<LoopChainAction>(toolStage.ToStage().Action).AgentProvider);
        var reopened = new JobChainsViewModel(null!, null!, null!, null!, null!);
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Verify", null, stages, DateTimeOffset.UtcNow));
        reopened.EditLoopAction(0);
        Assert.Equal(LoopChainAction.JevAgent, reopened.LoopAgentProvider);
        reopened.SaveLoopAction();
        Assert.Equal(LoopChainAction.JevAgent, Assert.IsType<LoopChainAction>(reopened.EdStageActions[0]).AgentProvider);
        reopened.OpenLoopActionAt(LoopChainAction.HumanType, 1);
        Assert.Equal(LoopChainAction.ExternalAgent, reopened.LoopAgentProvider);
    }

    [Fact]
    public async Task Qemu_step_settings_survive_editing_and_reopening()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, null!, null!);
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
        var reopened = new JobChainsViewModel(null!, null!, null!, null!, null!);
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
        var vm = new JobChainsViewModel(null!, null!, null!, null!, null!);
        vm.NewChain();
        var jobId = Guid.NewGuid();
        vm.AddStage(jobId);
        vm.EdStageGates[0] = new WaitGate(TimeSpan.FromSeconds(10));
        vm.OpenLoopActionAt(LoopChainAction.HumanType, 0);
        vm.SaveLoopAction();
        Assert.NotNull(vm.LoopError);
        Assert.Single(vm.EdStages);

        vm.LoopInstructions = "Review the draft";
        vm.SaveLoopAction();
        Assert.Equal(2, vm.EdStages.Count);
        Assert.Equal(jobId, Assert.Single(vm.EdStages[1]));
        Assert.IsType<WaitGate>(vm.EdStageGates[1]);
        vm.EditLoopAction(0);
        vm.LoopResponseField = "review";
        vm.LoopResponseType = LoopChainAction.YesNoResponse;
        vm.SaveLoopAction();
        Assert.Equal(2, vm.EdStages.Count);
        vm.MoveStage(0, 1);

        var stages = vm.BuildCanvasStageViews();
        var reopened = new JobChainsViewModel(null!, null!, null!, null!, null!);
        await reopened.OpenChainEditorAsync(new JobChainView(Guid.NewGuid(), Guid.NewGuid(), "Review", null, stages, DateTimeOffset.UtcNow));
        var action = Assert.IsType<LoopChainAction>(reopened.EdStageActions[1]);
        Assert.Equal(LoopChainAction.HumanType, action.Type);
        Assert.Equal("Review the draft", action.Instructions);
        Assert.Equal("review", action.ResponseField);
        Assert.Equal(LoopChainAction.YesNoResponse, action.ResponseType);
        Assert.IsType<WaitGate>(reopened.EdStageGates[0]);
    }
}
