using PlaceContext.Application.Workflows;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowMonitorViewModelTests
{
    [Fact]
    public async Task Opening_monitor_only_reads_data_and_does_not_submit_approval()
    {
        var stub = new Stub();
        using var vm = new WorkflowMonitorViewModel(stub, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Equal(1, stub.Reads);
        Assert.Equal(0, stub.Submits);
        var action = Assert.Single(vm.Snapshot!.Actions);
        vm.Propose(action, true);
        Assert.NotNull(vm.PendingConfirmation);
        Assert.Equal(0, stub.Submits);
        vm.Cancel();
        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(0, stub.Submits);
    }

    [Fact]
    public async Task A_confirmed_No_is_submitted_once_then_the_inbox_refreshes()
    {
        var stub = new Stub();
        using var vm = new WorkflowMonitorViewModel(stub, new PortalUiState());
        var projectId = Guid.NewGuid();
        await vm.LoadAsync(projectId);
        var action = Assert.Single(vm.Snapshot!.Actions);
        vm.Propose(action, false);
        await vm.ConfirmAsync();
        Assert.Equal(1, stub.Submits);
        Assert.Equal((projectId, action.RunId, action.RequestId, false), stub.Last);
        Assert.Equal(2, stub.Reads);
        Assert.Empty(vm.Snapshot!.Actions);
        Assert.Contains("does not necessarily stop", vm.Notice);
    }

    [Fact]
    public async Task A_stale_request_does_not_claim_to_have_been_approved()
    {
        var stub = new Stub { Fail = true };
        using var vm = new WorkflowMonitorViewModel(stub, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        vm.Propose(Assert.Single(vm.Snapshot!.Actions), true);
        await vm.ConfirmAsync();
        Assert.NotNull(vm.Error);
        Assert.Null(vm.Notice);
        Assert.Single(vm.Snapshot!.Actions);
    }

    [Fact]
    public async Task Accepted_response_remains_recorded_when_the_followup_refresh_fails()
    {
        var stub = new Stub { FailReadAfterSubmit = true };
        using var vm = new WorkflowMonitorViewModel(stub, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        vm.Propose(Assert.Single(vm.Snapshot!.Actions), true);
        await vm.ConfirmAsync();
        Assert.Equal(1, stub.Submits);
        Assert.Empty(vm.Snapshot!.Actions);
        Assert.Null(vm.Error);
        Assert.Contains("could not be refreshed", vm.Notice);
    }

    private sealed class Stub : IWorkflowMonitor
    {
        public bool Fail;
        public bool FailReadAfterSubmit;
        public int Reads;
        public int Submits;
        public (Guid Project, Guid Run, Guid Request, bool Answer) Last;
        private readonly WorkflowHumanAction _action = new(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "Lead onboarding", "Check the lead", DateTimeOffset.UtcNow,
            1, "approved", true, true);

        public Task<WorkflowMonitorSnapshot> LoadAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (FailReadAfterSubmit && Submits > 0) throw new InvalidOperationException("Refresh failed");
            return Task.FromResult(new WorkflowMonitorSnapshot(DateTimeOffset.UtcNow, 1, 1, 12, [],
                Submits > 0 ? [] : [_action], true));
        }
        public Task RespondAsync(Guid projectId, Guid runId, Guid requestId,
            bool approved, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Approval replaced");
            Last = (projectId, runId, requestId, approved);
            Submits++;
            return Task.CompletedTask;
        }
    }
}
