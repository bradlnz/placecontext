using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowApprovalDashboardTests
{
    [Fact]
    public async Task Pending_actions_load_without_model_calls_or_automatic_resumption()
    {
        var data = new DashboardUiStub();
        var inbox = new WorkflowActionInboxStub();
        using var vm = new PersonalDashboardsViewModel(data, new PortalUiState(), new DashboardTestJs(), actionInbox: inbox);
        var projectId = Guid.NewGuid();
        await vm.LoadAsync(projectId);
        Assert.Equal(1, vm.PendingHumanActionCount);
        Assert.Equal(0, data.DecisionCalls);
        Assert.Equal(projectId, inbox.ProjectId);
        vm.ProposeHumanResponse(inbox.Pending[0].ActionId, true);
        Assert.NotNull(vm.SelectedWorkflowAction);
        Assert.True(vm.ProposedApproval);
        vm.CancelHumanResponse();
        Assert.Null(vm.SelectedWorkflowAction);
        Assert.Empty(inbox.Responses);
    }

    [Fact]
    public async Task Confirmed_response_submits_only_the_selected_checkpoint_and_refreshes()
    {
        var inbox = new WorkflowActionInboxStub();
        using var vm = new PersonalDashboardsViewModel(new DashboardUiStub(), new PortalUiState(),
            new DashboardTestJs(), actionInbox: inbox);
        var projectId = Guid.NewGuid();
        await vm.LoadAsync(projectId);
        var action = Assert.Single(vm.WorkflowActions);
        vm.ProposeHumanResponse(action.ActionId, false);
        await vm.SubmitHumanResponseAsync();
        Assert.Equal((projectId, action.ChainRunId, action.RequestId, false), Assert.Single(inbox.Responses));
        Assert.Empty(vm.WorkflowActions);
        Assert.Contains("configured No", vm.ActionNotice);
    }

    [Fact]
    public async Task Failed_submission_does_not_claim_approval()
    {
        var inbox = new WorkflowActionInboxStub { Fail = true };
        using var vm = new PersonalDashboardsViewModel(new DashboardUiStub(), new PortalUiState(),
            new DashboardTestJs(), actionInbox: inbox);
        await vm.LoadAsync(Guid.NewGuid());
        vm.ProposeHumanResponse(inbox.Pending[0].ActionId, true);
        await vm.SubmitHumanResponseAsync();
        Assert.NotNull(vm.ActionError);
        Assert.Null(vm.ActionNotice);
        Assert.Single(vm.WorkflowActions);
        Assert.Empty(inbox.Responses);
    }

    private sealed class WorkflowActionInboxStub : IWorkflowActionInbox
    {
        public Guid ProjectId { get; private set; }
        public bool Fail;
        public List<WorkflowApprovalAction> Pending { get; } =
        [
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Lead onboarding",
                "Review lead qualification evidence before proceeding", DateTimeOffset.UtcNow, 3, "approved")
        ];
        public List<(Guid Project, Guid Run, Guid Request, bool Answer)> Responses { get; } = [];

        public Task<IReadOnlyList<WorkflowApprovalAction>> ListAsync(Guid projectId, CancellationToken ct = default)
        {
            ProjectId = projectId;
            return Task.FromResult<IReadOnlyList<WorkflowApprovalAction>>(Pending.ToArray());
        }
        public Task RespondAsync(Guid projectId, Guid chainRunId, Guid requestId, bool approved, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("Stale request");
            Responses.Add((projectId, chainRunId, requestId, approved));
            Pending.RemoveAll(x => x.ChainRunId == chainRunId && x.RequestId == requestId);
            return Task.CompletedTask;
        }
    }
}
