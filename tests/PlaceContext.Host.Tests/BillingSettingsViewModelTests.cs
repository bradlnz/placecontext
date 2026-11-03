using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class BillingSettingsViewModelTests
{
    [Theory]
    [InlineData("past_due")]
    [InlineData("canceled")]
    public async Task Existing_accounts_can_manage_payment_details_after_subscription_becomes_inactive(string status)
    {
        var allowance = new Allowance { Snapshot = Snapshot() with { Status = status, HasBillingAccount = true } };
        var portal = new Portal();
        var navigation = new Navigation();
        var vm = Create(allowance, portal, navigation);
        await vm.LoadAsync();
        Assert.True(vm.CanManage);
        Assert.True(vm.RunsUnavailable);
        await vm.ManageAsync();
        Assert.Equal(1, portal.ManagementCalls);
        Assert.Equal("https://workspace.example/", portal.ReturnBase?.AbsoluteUri);
        Assert.Equal("https://billing.example/manage", navigation.Destination);
        Assert.True(navigation.ForceLoad);
    }

    [Fact]
    public async Task Checkout_requires_a_loaded_managed_workspace_and_known_plan()
    {
        var allowance = new Allowance();
        var portal = new Portal();
        var vm = Create(allowance, portal, new Navigation());
        await vm.CheckoutAsync(RunPlans.Basic);
        Assert.Equal(0, portal.CheckoutCalls);
        await vm.LoadAsync();
        await vm.CheckoutAsync("unknown");
        Assert.Equal(0, portal.CheckoutCalls);
        await vm.CheckoutAsync(RunPlans.Standard);
        Assert.Equal(1, portal.CheckoutCalls);
        Assert.Equal(RunPlans.Standard, portal.Plan);

        allowance.Snapshot = Snapshot() with { Plan = RunPlans.Basic, Status = "active", HasBillingAccount = true };
        await vm.LoadAsync();
        Assert.False(vm.CanChoosePlan);
        await vm.CheckoutAsync(RunPlans.Standard);
        Assert.Equal(1, portal.CheckoutCalls);

        var local = Create(new Allowance(), portal, new Navigation(), managed: false);
        await local.LoadAsync();
        Assert.False(local.CanChoosePlan);
        await local.CheckoutAsync(RunPlans.Basic);
        Assert.Equal(1, portal.CheckoutCalls);
    }

    [Fact]
    public async Task Refresh_failure_clears_stale_billing_actions_and_successful_retry_recovers()
    {
        var allowance = new Allowance { Snapshot = Snapshot() with { HasBillingAccount = true } };
        var portal = new Portal();
        var vm = Create(allowance, portal, new Navigation());
        await vm.LoadAsync();
        allowance.Fail = true;
        await vm.LoadAsync();
        Assert.Null(vm.Current);
        Assert.NotNull(vm.Error);
        Assert.False(vm.CanManage);
        Assert.False(vm.CanChoosePlan);
        Assert.False(vm.Busy);
        await vm.ManageAsync();
        Assert.Equal(0, portal.ManagementCalls);
        allowance.Fail = false;
        await vm.LoadAsync();
        Assert.Null(vm.Error);
        Assert.True(vm.CanManage);
    }

    [Fact]
    public async Task Checkout_return_does_not_grant_execution_and_portal_failure_is_retryable()
    {
        var portal = new Portal { Fail = true };
        var navigation = new Navigation();
        var vm = Create(new Allowance(), portal, navigation);
        vm.SetParameters(RunPlans.Basic, "1", "success");
        await vm.LoadAsync();
        Assert.True(vm.Welcome);
        Assert.Contains("payment confirmation", vm.CheckoutNotice);
        Assert.True(vm.SubscriptionRequired);
        Assert.True(vm.RunsUnavailable);
        await vm.CheckoutAsync(RunPlans.Basic);
        Assert.NotNull(vm.Error);
        Assert.False(vm.Busy);
        Assert.Null(navigation.Destination);
        portal.Fail = false;
        await vm.CheckoutAsync(RunPlans.Basic);
        Assert.Null(vm.Error);
        Assert.NotNull(navigation.Destination);
        vm.SetParameters("unknown", null, "unknown");
        Assert.Null(vm.StartPlan);
        Assert.Null(vm.CheckoutNotice);
    }

    private static RunAllowanceSnapshot Snapshot() => new(RunPlans.Developer, "self_hosted_only", 7,
        DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), null);

    private static BillingSettingsViewModel Create(Allowance allowance, Portal portal, Navigation navigation,
        bool managed = true) => new(allowance, portal, navigation, new PortalUiState(),
            Options.Create(new RunBillingDeploymentOptions { ManagedHosting = managed }));

    private sealed class Allowance : IRunAllowance
    {
        public RunAllowanceSnapshot Snapshot { get; set; } = BillingSettingsViewModelTests.Snapshot();
        public bool Fail { get; set; }
        public Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default) => Fail
            ? Task.FromException<RunAllowanceSnapshot>(new InvalidOperationException("Unavailable"))
            : Task.FromResult(Snapshot);
        public Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class Portal : IRunBillingPortal
    {
        public Uri? EnterpriseSalesUrl => null;
        public int ManagementCalls { get; private set; }
        public int CheckoutCalls { get; private set; }
        public string? Plan { get; private set; }
        public Uri? ReturnBase { get; private set; }
        public bool Fail { get; set; }

        public Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default)
        {
            CheckoutCalls++;
            Plan = plan;
            ReturnBase = returnBase;
            return Fail ? Task.FromException<Uri>(new InvalidOperationException("Provider unavailable"))
                : Task.FromResult(new Uri("https://billing.example/checkout"));
        }

        public Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default)
        {
            ManagementCalls++;
            ReturnBase = returnBase;
            return Task.FromResult(new Uri("https://billing.example/manage"));
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public string? Destination { get; private set; }
        public bool ForceLoad { get; private set; }
        public Navigation() => Initialize("https://workspace.example/", "https://workspace.example/settings/billing");
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Destination = uri;
            ForceLoad = forceLoad;
        }
    }
}
