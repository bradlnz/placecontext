using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ParallelWorkflowEditorTests
{
    [Fact]
    public void Branch_selection_preserves_edits_and_tracks_add_remove_and_reopen()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, null!);
        vm.OpenParallelWorkflowAction(0);
        var first = vm.ParallelBranches[0];
        var second = vm.ParallelBranches[1];
        Assert.Same(first, vm.ActiveParallelBranch);
        first.WorkflowId = Guid.NewGuid().ToString();
        first.Conditions[0].Path = "data.status";
        vm.SelectParallelBranch(second);
        vm.SelectParallelBranch(first);
        Assert.Same(first, vm.ActiveParallelBranch);
        Assert.NotEmpty(first.WorkflowId);
        Assert.Equal("data.status", first.Conditions[0].Path);

        vm.AddParallelBranch();
        var added = vm.ParallelBranches[2];
        Assert.Same(added, vm.ActiveParallelBranch);
        vm.RemoveParallelBranch(first);
        Assert.Same(added, vm.ActiveParallelBranch);
        vm.RemoveParallelBranch(added);
        Assert.Equal(2, vm.ParallelBranches.Count);
        Assert.Same(added, vm.ActiveParallelBranch);

        vm.AddParallelBranch();
        vm.RemoveParallelBranch(vm.ActiveParallelBranch!);
        Assert.Same(added, vm.ActiveParallelBranch);
        vm.SelectParallelBranch(new());
        Assert.Same(added, vm.ActiveParallelBranch);

        vm.CloseParallelWorkflowEditor();
        vm.OpenParallelWorkflowAction(0);
        Assert.Same(vm.ParallelBranches[0], vm.ActiveParallelBranch);
        Assert.Equal(2, vm.ParallelBranches.Count);
    }

    [Fact]
    public void Adding_at_the_branch_limit_keeps_the_current_selection()
    {
        var vm = new JobChainsViewModel(null!, null!, null!, null!);
        vm.OpenParallelWorkflowAction(0);
        for (var i = 0; i < 6; i++) vm.AddParallelBranch();
        vm.SelectParallelBranch(vm.ParallelBranches[0]);
        vm.AddParallelBranch();
        Assert.Equal(8, vm.ParallelBranches.Count);
        Assert.Same(vm.ParallelBranches[0], vm.ActiveParallelBranch);
    }
}
