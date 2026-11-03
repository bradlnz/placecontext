using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Cluster;
using PlaceContext.Application.Cqrs;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class OnboardingViewModelTests
{
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

    [Fact]
    public void Signup_plan_is_preserved_for_billing_and_unrecognized_values_are_ignored()
    {
        var vm = Create(new ClusterQueries(), new Navigation());
        vm.SetParameters(RunPlans.Standard);
        Assert.True(vm.ManagedHosting);
        Assert.Equal("/settings/billing?welcome=1&start=standard", vm.BillingUrl);
        vm.SetParameters("https://example.com");
        Assert.Equal(PageRoutes.BillingSettings, vm.BillingUrl);
    }

    [Theory]
    [InlineData("/cluster")]
    [InlineData("/settings/cluster")]
    public void Cluster_navigation_stays_active_for_existing_and_settings_routes(string path)
    {
        var vm = new SettingsLayoutViewModel(new Navigation(path), null!, null!);
        Assert.True(vm.IsActive("cluster"));
        Assert.False(vm.IsActive("billing"));
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

    private static OnboardingViewModel Create(ClusterQueries queries, Navigation navigation) =>
        new(new PlaceContextService(queries, null!, null!, null!),
            Options.Create(new RunBillingDeploymentOptions { ManagedHosting = true }),
            navigation, new PortalUiState());

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
    }
}
