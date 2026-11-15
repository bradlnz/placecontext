using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowSchedulesTests
{
    [Fact]
    public async Task Workflow_tab_keeps_its_target_and_filter_after_schedule_changes()
    {
        var dispatcher = new SchedulesDispatcher();
        var vm = new SchedulesViewModel(new PlaceContextService(dispatcher, null!, null!, null!),
            new PortalUiState(), new FakeCurrentTenant(Guid.NewGuid()));
        await vm.LoadAsync(dispatcher.ProjectId, dispatcher.WorkflowId);
        Assert.Null(vm.Message);
        Assert.Equal(dispatcher.WorkflowId, vm.NewWorkflowId);
        Assert.Equal("Workflow", vm.WorkflowName);
        Assert.Single(vm.Triggers!);
        vm.NewName = "Daily";
        vm.NewWorkflowId = Guid.NewGuid(); // A stale form selection must not redirect a workflow's schedule.
        await vm.AddTriggerAsync(dispatcher.ProjectId);
        Assert.Null(vm.Error);
        Assert.Equal(dispatcher.WorkflowId, Assert.IsType<CreateTriggerCommand>(dispatcher.LastCommand).WorkflowId);
        Assert.Single(vm.Triggers!);
        await vm.ToggleAsync(dispatcher.ProjectId, vm.Triggers![0]);
        Assert.Single(vm.Triggers!);
        vm.StartEdit(vm.Triggers![0]);
        await vm.SaveEditAsync(dispatcher.ProjectId);
        Assert.Null(vm.EditError);
        Assert.Single(vm.Triggers!);
        await vm.RemoveAsync(dispatcher.ProjectId, vm.Triggers![0].Id);
        Assert.Single(vm.Triggers!);
        await vm.LoadAsync(dispatcher.ProjectId);
        Assert.Equal(2, vm.Triggers!.Count);
    }

    private sealed class SchedulesDispatcher : IDispatcher
    {
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid WorkflowId { get; } = Guid.NewGuid();
        public object? LastCommand;
        private TriggerView Trigger(Guid workflowId) => new(Guid.NewGuid(), ProjectId, null, "Daily", "Schedule", true,
            "0 9 * * *", null, DateTimeOffset.UtcNow.AddDays(1), null, DateTimeOffset.UtcNow, workflowId);
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            LastCommand = command;
            return Task.FromResult(typeof(T) == typeof(bool) ? (T)(object)true : (T)(object)Trigger(WorkflowId));
        }
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)(query switch
        {
            ListJobChainsQuery => (object)new JobChainView[] { new(WorkflowId, ProjectId, "Workflow", null, [], DateTimeOffset.UtcNow) },
            ListJobsQuery => Array.Empty<JobView>(),
            ListTriggersQuery => new[] { Trigger(WorkflowId), Trigger(Guid.NewGuid()) },
            ListEventTypesQuery => Array.Empty<EventTypeView>(),
            _ => throw new NotSupportedException(query.GetType().Name)
        }));
    }
}
