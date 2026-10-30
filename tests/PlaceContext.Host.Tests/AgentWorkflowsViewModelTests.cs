using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class AgentWorkflowsViewModelTests
{
    [Fact]
    public async Task Subscription_controls_unlock_run_and_transport_retries_keep_the_request_id()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid()); vm.Select(service.Workflow);
        Assert.False(vm.CanRun);
        await vm.ToggleSubscriptionAsync(service.Workflow);
        Assert.True(vm.CanRun); vm.Inputs["client"] = "Customer";
        service.FailOnce = true; await vm.RunAsync();
        Assert.NotNull(vm.Error); await vm.RunAsync();
        Assert.Null(vm.Error); Assert.NotNull(vm.Result);
        Assert.Equal(service.RequestIds[0], service.RequestIds[1]);
        await vm.RunAsync(); Assert.NotEqual(service.RequestIds[1], service.RequestIds[2]);
        await vm.ToggleSubscriptionAsync(service.Workflow); Assert.False(vm.CanRun);
    }

    [Fact]
    public async Task Double_click_does_not_start_another_run_and_project_navigation_waits_for_current_work()
    {
        var service = new AgentWorkflowUiStub { Active = true, Pause = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        var firstProject = Guid.NewGuid(); var secondProject = Guid.NewGuid();
        await vm.LoadAsync(firstProject); vm.Select(service.Workflow);
        var running = vm.RunAsync(); await service.Started.Task;
        await vm.RunAsync(); Assert.Equal(1, service.Runs);
        var navigation = vm.LoadAsync(secondProject);
        Assert.False(navigation.IsCompleted); Assert.Equal(firstProject, vm.ProjectId);
        service.Pause.TrySetResult(); await running; await navigation;
        Assert.Equal(secondProject, vm.ProjectId); Assert.Null(vm.Result); Assert.Null(vm.Selected);
        vm.Dispose(); vm.Dispose();
    }
}
