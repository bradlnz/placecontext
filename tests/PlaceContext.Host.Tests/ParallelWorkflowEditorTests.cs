using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Tests;

public sealed class ParallelWorkflowEditorTests
{
    [Fact]
    public async Task Deleting_branches_preserves_survivors_and_removes_only_the_empty_routing_step()
    {
        var store = new Store();
        var vm = await Editor(store);
        var branches = new ConditionalWorkflowBranch[] {
            new(store.Existing.Id, "First", "eq:kind:a"), new(store.Existing.Id, "Second", "eq:kind:b", true),
            new(store.Existing.Id, "Third", "eq:kind:c") };
        var gate = new ConditionGate("eq:ready:true");
        var policy = new StepExecutionPolicy(2, 5, 60);
        vm.NewChain("Routes", [new([], gate, action: new ParallelWorkflowsChainAction(branches, "results"), executionPolicy: policy),
            new([Guid.NewGuid()], gate, executionPolicy: policy)]);
        vm.DeleteWorkflowBranch(0, 0);
        var remaining = Assert.IsType<ParallelWorkflowsChainAction>(vm.EdStageActions[0]);
        Assert.Equal(branches[1..], remaining.Branches);
        Assert.Equal("results", remaining.OutputField);
        Assert.Same(gate, vm.EdStageGates[0]);
        Assert.Same(policy, vm.EdStagePolicies[0]);
        vm.DeleteWorkflowBranch(0, 1);
        remaining = Assert.IsType<ParallelWorkflowsChainAction>(vm.EdStageActions[0]);
        Assert.Equal(branches[1], Assert.Single(remaining.Branches));
        vm.OpenParallelWorkflowAction(0, true);
        Assert.True(Assert.Single(vm.ParallelBranches).WhenFalse);
        await vm.SaveParallelWorkflowActionAsync();
        Assert.Null(vm.ParallelWorkflowError);
        Assert.Equal(2, vm.EdStages.Count);
        vm.DeleteWorkflowBranch(0, 0);
        Assert.Single(vm.EdStages);
        Assert.NotEmpty(vm.EdStages[0]);
        Assert.Same(gate, vm.EdStageGates[0]);
        Assert.Same(policy, vm.EdStagePolicies[0]);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task Removing_a_branch_in_the_modal_is_discarded_on_cancel()
    {
        var store = new Store();
        var vm = await Editor(store);
        var original = new ParallelWorkflowsChainAction([new(store.Existing.Id, "First", "true"), new(store.Existing.Id, "Second", "false")]);
        vm.NewChain("Routes", [ChainStage.ForAction(original)]);
        vm.OpenParallelWorkflowAction(0, true);
        vm.RemoveParallelBranch(vm.ParallelBranches[0]);
        Assert.Single(vm.ParallelBranches);
        vm.CloseParallelWorkflowEditor();
        Assert.Same(original, vm.EdStageActions[0]);
        vm.OpenStepBranches(0);
        Assert.Equal(3, vm.ParallelBranches.Count);
        Assert.Same(vm.ParallelBranches[2], vm.ActiveParallelBranch);
        Assert.Throws<ArgumentException>(() => new ParallelWorkflowsChainAction([]));
    }

    [Fact]
    public async Task Expanding_a_split_preserves_both_paths_and_only_saves_valid_branch_drafts()
    {
        var store = new Store();
        var vm = await Editor(store);
        var query = new QueryDataChainAction("SELECT 1", "rows");
        var fallback = new ChainStage([Guid.NewGuid()], new ConditionGate("eq:retry:true"), executionPolicy: new(1, 2, 60));
        var gate = new ConditionGate("all:[\"eq:ready:true\",\"gt:score:0.5\"]", [fallback]);
        var policy = new StepExecutionPolicy(2, 5, 90);
        vm.NewChain("Routing", [new([], gate, action: query, executionPolicy: policy)]);
        vm.OpenStepBranches(0);
        Assert.Equal(3, vm.ParallelBranches.Count);
        Assert.Same(gate, vm.EdStageGates[0]);
        vm.CloseParallelWorkflowEditor();
        Assert.Empty(store.Created);
        Assert.Same(query, vm.EdStageActions[0]);

        vm.OpenStepBranches(0);
        await vm.SaveParallelWorkflowActionAsync();
        Assert.NotNull(vm.ParallelWorkflowError);
        Assert.Empty(store.Created);
        Assert.Same(query, vm.EdStageActions[0]);
        vm.ParallelBranches[2].WorkflowId = store.Existing.Id.ToString();
        await vm.SaveParallelWorkflowActionAsync();
        Assert.Null(vm.ParallelWorkflowError);
        Assert.False(vm.ShowParallelWorkflowEditor);
        Assert.Equal(2, store.Created.Count);
        var action = Assert.IsType<ParallelWorkflowsChainAction>(vm.EdStageActions[0]);
        Assert.Equal(3, action.Branches.Count);
        Assert.Equal(gate.Expression, action.Branches[0].Condition);
        Assert.Equal(gate.Expression, action.Branches[1].Condition);
        Assert.False(action.Branches[0].WhenFalse);
        Assert.True(action.Branches[1].WhenFalse);
        Assert.Same(query, store.Created[0].StageActions![0]);
        Assert.Equal(policy, store.Created[0].StagePolicies![0]);
        Assert.Null(store.Created[0].StageGates![0]);
        Assert.Equal(fallback.JobIds, store.Created[1].Stages![0]);
        Assert.Same(fallback.Gate, store.Created[1].StageGates![0]);
        Assert.Same(fallback.ExecutionPolicy, store.Created[1].StagePolicies![0]);
        Assert.False(vm.EdStageGates.ContainsKey(0));
        Assert.False(vm.EdStagePolicies.ContainsKey(0));
        vm.OpenParallelBranch(0, 1);
        Assert.True(vm.ActiveParallelBranch!.WhenFalse);
        Assert.Equal(gate.Expression, vm.ActiveParallelBranch.Expression);
    }

    [Fact]
    public async Task Retrying_a_partial_save_reuses_already_created_branch_workflows()
    {
        var store = new Store { FailCreateAttempt = 2 };
        var vm = await Editor(store);
        vm.NewChain("Routing", [new([Guid.NewGuid()], new ConditionGate("true", [ChainStage.Of(Guid.NewGuid())]))]);
        vm.OpenStepBranches(0);
        vm.ParallelBranches[2].WorkflowId = store.Existing.Id.ToString();
        await vm.SaveParallelWorkflowActionAsync();
        Assert.True(vm.ShowParallelWorkflowEditor);
        Assert.NotNull(vm.ParallelWorkflowError);
        Assert.Single(store.Created);
        Assert.NotEmpty(vm.EdStages[0]);
        await vm.SaveParallelWorkflowActionAsync();
        Assert.Null(vm.ParallelWorkflowError);
        Assert.Equal(2, store.Created.Count);
        Assert.Equal(3, store.CreateAttempts);
        Assert.Empty(vm.EdStages[0]);
    }

    [Fact]
    public async Task Pathways_after_a_human_step_preserve_the_step_and_shift_following_limits()
    {
        var store = new Store();
        var vm = await Editor(store);
        var human = new LoopChainAction(LoopChainAction.HumanType, "Approve", "approved");
        var policy = new StepExecutionPolicy(2, 5, 90);
        vm.NewChain("Approval", [ChainStage.ForAction(human), new([Guid.NewGuid()], executionPolicy: policy)]);
        Assert.False(vm.CanBranchStep(0));
        vm.OpenStepPathways(0);
        foreach (var branch in vm.ParallelBranches) branch.WorkflowId = store.Existing.Id.ToString();
        await vm.SaveParallelWorkflowActionAsync();
        Assert.Equal(3, vm.EdStages.Count);
        Assert.Same(human, vm.EdStageActions[0]);
        Assert.IsType<ParallelWorkflowsChainAction>(vm.EdStageActions[1]);
        Assert.Equal(policy, vm.EdStagePolicies[2]);
        vm.OpenStepPathways(0);
        await vm.SaveParallelWorkflowActionAsync();
        Assert.Equal(3, vm.EdStages.Count);
    }

    private static async Task<JobChainsViewModel> Editor(Store store)
    {
        var vm = new JobChainsViewModel(new PlaceContextService(store, null!, null!, null!), null!, null!, null!) { Embedded = true };
        vm.Initialize(store.Existing.ProjectId);
        await vm.RefreshSubWorkflowAsync();
        return vm;
    }

    private sealed class Store : IDispatcher
    {
        public JobChainView Existing { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "Existing", null,
            [new([], Action: new QueryDataChainActionView("SELECT 2", "rows"))], DateTimeOffset.UtcNow);
        public List<CreateJobChainCommand> Created { get; } = [];
        public int CreateAttempts;
        public int FailCreateAttempt;
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)(query switch
        {
            ListJobChainsQuery => (object)new[] { Existing },
            ListJobsQuery => Array.Empty<JobView>(),
            _ => throw new NotSupportedException(query.GetType().Name)
        }));
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            var create = Assert.IsType<CreateJobChainCommand>(command);
            if (++CreateAttempts == FailCreateAttempt) throw new InvalidOperationException("Save failed");
            Created.Add(create);
            return Task.FromResult((T)(object)new JobChainView(Guid.NewGuid(), create.ProjectId, create.Name, null, [], DateTimeOffset.UtcNow));
        }
    }

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
        Assert.Single(vm.ParallelBranches);
        Assert.Same(second, vm.ActiveParallelBranch);

        vm.AddParallelBranch();
        vm.RemoveParallelBranch(vm.ActiveParallelBranch!);
        Assert.Same(second, vm.ActiveParallelBranch);
        vm.SelectParallelBranch(new());
        Assert.Same(second, vm.ActiveParallelBranch);

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
