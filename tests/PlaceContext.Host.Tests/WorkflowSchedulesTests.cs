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
        Assert.False(vm.ShowAddSchedule);
        vm.OpenAddSchedule();
        Assert.True(vm.ShowAddSchedule);
        vm.NewName = "Daily";
        vm.NewWorkflowId = Guid.NewGuid(); // A stale form selection must not redirect a workflow's schedule.
        await vm.AddTriggerAsync(dispatcher.ProjectId);
        Assert.Null(vm.Error);
        Assert.False(vm.ShowAddSchedule);
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

    [Fact]
    public async Task Add_pane_stays_open_on_validation_or_save_failure_and_cancel_does_not_create()
    {
        var dispatcher = new SchedulesDispatcher();
        var vm = new SchedulesViewModel(new PlaceContextService(dispatcher, null!, null!, null!),
            new PortalUiState(), new FakeCurrentTenant(Guid.NewGuid()));
        await vm.LoadAsync(dispatcher.ProjectId);
        vm.OpenAddSchedule();
        await vm.AddTriggerAsync(dispatcher.ProjectId);
        Assert.Equal("Name is required.", vm.Error);
        Assert.True(vm.ShowAddSchedule);
        vm.NewName = "Daily";
        await vm.AddTriggerAsync(dispatcher.ProjectId);
        Assert.Equal("Pick a workflow.", vm.Error);
        Assert.True(vm.ShowAddSchedule);
        vm.CloseAddSchedule();
        Assert.False(vm.ShowAddSchedule);
        Assert.Null(dispatcher.LastCommand);
        vm.OpenAddSchedule();
        Assert.Null(vm.Error);
        vm.NewWorkflowId = dispatcher.WorkflowId;
        dispatcher.FailCreate = true;
        await vm.AddTriggerAsync(dispatcher.ProjectId);
        Assert.Equal("Schedule could not be saved.", vm.Error);
        Assert.True(vm.ShowAddSchedule);
        Assert.False(vm.Busy);
        Assert.Equal("Daily", vm.NewName);
    }

    private sealed class SchedulesDispatcher : IDispatcher
    {
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid WorkflowId { get; } = Guid.NewGuid();
        public object? LastCommand;
        public bool FailCreate;
        private TriggerView Trigger(Guid workflowId) => new(Guid.NewGuid(), ProjectId, null, "Daily", "Schedule", true,
            "0 9 * * *", null, DateTimeOffset.UtcNow.AddDays(1), null, DateTimeOffset.UtcNow, workflowId);
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            LastCommand = command;
            if (FailCreate && command is CreateTriggerCommand) throw new InvalidOperationException("Schedule could not be saved.");
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
