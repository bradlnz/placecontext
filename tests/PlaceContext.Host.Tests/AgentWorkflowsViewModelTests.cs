using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class AgentWorkflowsViewModelTests
{
    [Fact]
    public async Task Store_filters_combine_search_industry_and_subscription()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Equal(2, vm.VisibleWorkflows.Count());
        vm.Browse(AgentBrowseFilter.Free); Assert.False(Assert.Single(vm.VisibleWorkflows).Paid);
        vm.Search = "ONBOARDING"; Assert.False(Assert.Single(vm.VisibleWorkflows).Paid);
        vm.Browse(AgentBrowseFilter.All); Assert.Equal(2, vm.VisibleWorkflows.Count());
        Assert.Equal(2, Assert.Single(vm.VisibleUseCases).Variants.Count);
        vm.Industry = "Retail"; Assert.Empty(vm.VisibleWorkflows);
        vm.Industry = ""; vm.Search = "";
        vm.Browse(AgentBrowseFilter.Subscribed); Assert.Empty(vm.VisibleWorkflows);
        await vm.ToggleSubscriptionAsync(service.Workflow); Assert.Single(vm.VisibleWorkflows);
        vm.Select(service.Workflow); vm.CloseDetails(); Assert.Null(vm.Selected);
    }

    [Fact]
    public async Task Local_checks_filter_preserves_the_canonical_use_case_outcome()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        vm.Browse(AgentBrowseFilter.Free);
        Assert.False(Assert.Single(vm.VisibleWorkflows).Paid);
        var useCase = Assert.Single(vm.VisibleUseCases);
        Assert.Equal(service.Workflow.Outcome, useCase.Summary.Outcome);
        Assert.NotEqual(service.FreeWorkflow.Outcome, useCase.Summary.Outcome);
    }

    [Fact]
    public async Task Context_discovery_does_not_run_or_adopt_candidate_data_until_selected()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        await vm.SelectUseCaseAsync(service.Workflow);
        Assert.False(vm.Selected!.Paid);
        Assert.Equal(1, service.Preparations);
        Assert.Equal(0, service.Runs);
        Assert.True(vm.HasPreparedContext);
        Assert.False(vm.HasContext);
        Assert.False(vm.ProjectContextApplied);
        Assert.Single(vm.MissingContextFields);
        Assert.False(vm.CanStart);
        await vm.RunAsync();
        Assert.Equal(0, service.Runs);
        vm.UseProjectContext();
        Assert.True(vm.ProjectContextApplied);
        Assert.Empty(vm.MissingContextFields);
        Assert.Equal("Customer from project data", vm.Inputs["client"]);
        Assert.Equal(0, service.Runs);
        await vm.RunAsync();
        Assert.Equal(1, service.Runs);
        Assert.NotNull(vm.Result);
        vm.SetInput("client", "Changed context");
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task Failed_project_navigation_clears_previous_workspace_and_can_retry()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        Assert.NotNull(vm.Workspace);
        var unavailable = Guid.NewGuid();
        service.UnavailableProject = unavailable;
        await vm.LoadAsync(unavailable);
        Assert.Null(vm.Workspace);
        Assert.NotNull(vm.Error);
        service.UnavailableProject = null;
        await vm.LoadAsync(unavailable);
        Assert.NotNull(vm.Workspace);
        Assert.Null(vm.Error);
        Assert.Equal(3, service.Loads);
    }

    [Fact]
    public async Task Switching_agent_mode_retains_reviewed_context_and_build_link_excludes_it()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        await vm.SelectUseCaseAsync(service.Workflow);
        vm.UseProjectContext();
        vm.Inputs["client"] = "Private customer detail";
        await vm.SelectAsync(service.Workflow);
        Assert.True(vm.ProjectContextApplied);
        Assert.Equal("Private customer detail", vm.Inputs["client"]);
        Assert.Equal(1, service.Preparations);
        var link = Uri.UnescapeDataString(vm.BuildAgentUrl);
        Assert.Contains("Client onboarding", link);
        Assert.DoesNotContain("Private customer detail", link);
        Assert.DoesNotContain("Customer from project data", link);
        vm.CloseDetails();
        Assert.Null(vm.Preparation);
        Assert.False(vm.ProjectContextApplied);
    }

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
        vm.Inputs["client"] = "Customer";
        var running = vm.RunAsync(); await service.Started.Task;
        await vm.RunAsync(); Assert.Equal(1, service.Runs);
        var navigation = vm.LoadAsync(secondProject);
        Assert.False(navigation.IsCompleted); Assert.Equal(firstProject, vm.ProjectId);
        service.Pause.TrySetResult(); await running; await navigation;
        Assert.Equal(secondProject, vm.ProjectId); Assert.Null(vm.Result); Assert.Null(vm.Selected);
        vm.Dispose(); vm.Dispose();
    }
    [Fact]
    public async Task Mini_app_uses_one_authorised_connector_then_asks_for_a_name_without_running()
    {
        var service = new AgentWorkflowUiStub();
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        var projectId = Guid.NewGuid();
        await vm.LoadAsync(projectId);
        await vm.SelectUseCaseAsync(service.Workflow);
        Assert.Equal("Clients", vm.ConnectedSourceName); // only one matched source
        Assert.True(vm.CanGenerateWorkflow);
        Assert.False(vm.NamingWorkflow);
        Assert.False(vm.ProjectContextApplied);
        Assert.False(vm.HasContext);
        vm.AskWorkflowName();
        Assert.True(vm.NamingWorkflow);
        vm.WorkflowName = "Customer follow up";
        var draft = Uri.UnescapeDataString(Assert.IsType<string>(vm.NamedWorkflowDraftUrl));
        Assert.Contains("Customer follow up", draft);
        Assert.Contains("Clients", draft);
        Assert.Contains("verifying", draft, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(projectId.ToString(), draft);
        Assert.DoesNotContain("Customer from project data", draft);
        Assert.DoesNotContain("One candidate client record", draft);
        Assert.Equal(0, service.Runs);
        vm.WorkflowName = "ab";
        Assert.Null(vm.NamedWorkflowDraftUrl);
        vm.WorkflowName = new string('x', 101);
        Assert.Null(vm.NamedWorkflowDraftUrl);
        vm.CancelWorkflowName();
        Assert.Null(vm.NamedWorkflowDraftUrl);
        Assert.False(vm.NamingWorkflow);
    }

    [Fact]
    public async Task Multiple_project_connectors_require_an_explicit_selection_and_reset_between_apps()
    {
        var service = new AgentWorkflowUiStub();
        service.Preparation = service.Preparation with { Sources =
        [
            new("Clients", "Project table", null, "Client records"),
            new("Calls", "Project table", null, "Call records")
        ] };
        using var vm = new AgentWorkflowsViewModel(service, new PortalUiState());
        await vm.LoadAsync(Guid.NewGuid());
        await vm.SelectUseCaseAsync(service.Workflow);
        Assert.Equal(2, vm.ConnectorSources.Count);
        Assert.Empty(vm.ConnectedSourceName);
        Assert.False(vm.CanGenerateWorkflow);
        vm.ConnectSource("Unapproved third-party API");
        Assert.False(vm.CanGenerateWorkflow);
        vm.ConnectSource("Calls");
        Assert.True(vm.CanGenerateWorkflow);
        vm.AskWorkflowName();
        vm.WorkflowName = "Phone service insights";
        Assert.Contains("Calls", Uri.UnescapeDataString(vm.NamedWorkflowDraftUrl!));
        await vm.SelectAsync(service.Workflow);
        Assert.Equal("Calls", vm.ConnectedSourceName);
        vm.CloseDetails();
        Assert.False(vm.CanGenerateWorkflow);
        Assert.False(vm.NamingWorkflow);
        Assert.Equal(0, service.Runs);
    }

}
