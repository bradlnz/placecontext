using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cluster;
using PlaceContext.Application.Cqrs;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class OnboardingViewModelTests
{
    [Fact]
    public async Task Managed_onboarding_opens_cloud_status_without_manual_cluster_setup()
    {
        var navigation = new Navigation();
        var queries = new ClusterQueries();
        var vm = new OnboardingViewModel(new PlaceContextService(queries, null!, null!, null!), navigation,
            new PortalUiState(), new ModelConnections(), Microsoft.Extensions.Options.Options.Create(
                new PlaceContext.Application.Billing.RunBillingDeploymentOptions { ManagedHosting = true }));
        await vm.LoadAsync();
        Assert.Equal("/cloud", navigation.Destination);
        Assert.Equal(0, queries.Calls);
    }

    [Theory]
    [InlineData(ExecutionLocation.Cloud, "#cloud-workers")]
    [InlineData(ExecutionLocation.Local, "#install-locally")]
    public async Task Setup_requires_an_explicit_location_and_uses_existing_cluster_settings(
        ExecutionLocation location, string instructions)
    {
        var queries = new ClusterQueries();
        var navigation = new Navigation();
        var vm = Create(queries, navigation);

        await vm.LoadAsync();
        Assert.False(vm.CanContinue);
        vm.Continue();
        Assert.Equal(SetupGuideStep.Location, vm.Step);
        vm.SelectLocation((ExecutionLocation)99);
        Assert.False(vm.CanContinue);

        vm.SelectLocation(location);
        vm.Continue();
        Assert.Equal(SetupGuideStep.Cluster, vm.Step);
        Assert.EndsWith(instructions, vm.SetupUrl);
        Assert.Contains("no visible nodes", vm.ClusterSummary);
        vm.OpenCluster();
        Assert.Equal(PageRoutes.ClusterSettings, navigation.Destination);
        vm.Back();
        Assert.Equal(SetupGuideStep.Location, vm.Step);
        Assert.Equal(location, vm.Location);
        Assert.Equal(1, queries.Calls);
    }

    [Fact]
    public async Task Failed_cluster_refresh_does_not_show_stale_health_or_block_setup_instructions()
    {
        var queries = new ClusterQueries { Cluster = new(false, []) };
        var vm = Create(queries, new Navigation());
        await vm.LoadAsync();
        Assert.Contains("reports only its local host", vm.ClusterSummary);
        queries.Fail = true;
        await vm.LoadAsync();
        Assert.NotNull(vm.Error);
        Assert.Null(vm.Cluster);
        Assert.False(vm.Loading);
        vm.SelectLocation(ExecutionLocation.Local);
        vm.Continue();
        Assert.Equal(SetupGuideStep.Cluster, vm.Step);
        queries.Fail = false;
        await vm.LoadAsync();
        Assert.Null(vm.Error);
        Assert.NotNull(vm.Cluster);
    }

    [Theory]
    [InlineData("/cluster")]
    [InlineData("/settings/cluster")]
    public void Cluster_navigation_stays_active_for_existing_and_settings_routes(string path)
    {
        var vm = new SettingsLayoutViewModel(new Navigation(path), null!, null!);
        Assert.True(vm.IsActive("cluster"));
        Assert.False(vm.IsActive("backup"));
    }

    [Fact]
    public void Settings_groups_do_not_expose_admin_links_before_authorization()
    {
        var vm = new SettingsLayoutViewModel(new Navigation("/settings/api-tokens"), null!, null!);
        var group = Assert.Single(vm.VisibleGroups);
        Assert.Equal("Access", group.Key);
        Assert.Equal(SettingsLayoutViewModel.ApiTokensPath, Assert.Single(group).Path);
        Assert.Equal("API tokens", vm.ActiveLabel);
    }

    [Fact]
    public async Task Setup_finishes_in_the_current_project_without_requiring_a_model()
    {
        var ui = new PortalUiState();
        var project = Guid.NewGuid();
        ui.SetProject(project, "First project");
        var navigation = new Navigation();
        var vm = new OnboardingViewModel(new PlaceContextService(new ClusterQueries(), null!, null!, null!),
            navigation, ui, new ModelConnections());
        await vm.LoadAsync();
        vm.Back();
        Assert.Equal(SetupGuideStep.Location, vm.Step);
        vm.SelectLocation(ExecutionLocation.Local);
        vm.Continue();
        vm.Continue();
        vm.Continue();
        Assert.Equal(SetupGuideStep.Ready, vm.Step);
        Assert.False(vm.HasModelSetup);
        Assert.Equal("First project", vm.WorkspaceName);
        Assert.Equal(PageRoutes.ProjectWorkflows(project), vm.WorkflowsUrl);
        Assert.Equal(PageRoutes.ProjectData(project), vm.DataUrl);
        vm.SkipGuide();
        Assert.Equal($"/project/{project}/dashboard", navigation.Destination);
        vm.Back();
        Assert.Equal(SetupGuideStep.Cluster, vm.Step);
    }

    [Fact]
    public async Task Model_checks_are_independent_of_cluster_failures_and_clear_stale_state()
    {
        var models = new ModelConnections
        {
            Connections = [new(Guid.NewGuid(), "Provider", "openai", "https://example.test", ["model"], true, true)]
        };
        var vm = new OnboardingViewModel(new PlaceContextService(new ClusterQueries { Fail = true }, null!, null!, null!),
            new Navigation(), new PortalUiState(), models);
        await vm.LoadAsync();
        Assert.NotNull(vm.Error);
        Assert.True(vm.HasModelSetup);
        Assert.Null(vm.ModelError);
        models.Fail = true;
        await vm.LoadAsync();
        Assert.False(vm.HasModelSetup);
        Assert.NotNull(vm.ModelError);
        Assert.False(vm.Loading);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    public async Task Execution_status_requires_a_real_ready_worker(bool real, bool ready, bool controlPlane, bool expected)
    {
        var node = new ClusterNode("node", [], ready, "v1", "linux", "amd64", null, null, null, null, false, controlPlane);
        var vm = Create(new ClusterQueries { Cluster = new(real, [node]) }, new Navigation());
        await vm.LoadAsync();
        Assert.Equal(expected, vm.HasReadyWorker);
    }

    private static OnboardingViewModel Create(ClusterQueries queries, Navigation navigation) =>
        new(new PlaceContextService(queries, null!, null!, null!), navigation, new PortalUiState(), new ModelConnections());

    private sealed class ModelConnections : IModelConnections
    {
        public IReadOnlyList<ModelConnection> Connections { get; init; } = [];
        public bool Fail { get; set; }
        public Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default) =>
            Fail ? throw new InvalidOperationException("Provider unavailable") : Task.FromResult(Connections);
        public Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class ClusterQueries : IDispatcher
    {
        public ClusterInfo Cluster { get; set; } = new(true, []);
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            Assert.IsType<GetClusterInfoQuery>(query);
            Calls++;
            if (Fail)
            {
                throw new InvalidOperationException("Cluster unavailable");
            }
            return Task.FromResult((TResult)(object)Cluster);
        }

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) =>
            throw new InvalidOperationException("Onboarding must not mutate deployment state.");
    }

    private sealed class Navigation : NavigationManager
    {
        public string? Destination { get; private set; }
        public Navigation(string path = "/onboarding") => Initialize("https://workspace.example/", "https://workspace.example" + path);
        protected override void NavigateToCore(string uri, bool forceLoad) => Destination = uri;
        protected override void NavigateToCore(string uri, NavigationOptions options) => Destination = uri;
    }
}
