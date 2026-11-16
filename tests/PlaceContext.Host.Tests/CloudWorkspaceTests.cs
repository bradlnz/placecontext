using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class CloudWorkspaceTests
{
    [Fact]
    public async Task Active_workspace_displays_exhausted_usage_and_subscription_management()
    {
        using var services = Services(new Workspace { Status = new("ready", "basic", 1, null) }, new Billing(),
            usage: new("basic", "active", 2, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29),
                60, true, 2, 3, 3));
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.CloudWorkspace>()).ToHtmlString());
        Assert.Contains("2 / 2", html);
        Assert.Contains("3 / 3", html);
        Assert.Contains("A usage limit has been reached", html);
        Assert.Contains("Manage subscription", html);
        Assert.DoesNotContain("Subscribe to Basic", html);
    }

    [Theory]
    [InlineData("pending", 0, "Setting up", false)]
    [InlineData("ready", 1, "Ready", false)]
    [InlineData("failed", 2, "Setup failed", false)]
    [InlineData("failed", 5, "Setup failed", true)]
    public async Task Cloud_page_shows_setup_status_usage_limits_and_authorized_retry(string state, int attempts, string label, bool retry)
    {
        var workspace = new Workspace { Status = new(state, "basic", attempts, attempts < 5 ? DateTimeOffset.UtcNow.AddMinutes(1) : null) };
        var billing = new Billing();
        using var services = Services(workspace, billing);
        var vm = services.GetRequiredService<CloudWorkspaceViewModel>();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.CloudWorkspace>()).ToHtmlString());
        Assert.Contains(label, html);
        Assert.Equal(retry, html.Contains("Retry setup"));
        Assert.Contains("10,000", html);
        Assert.Contains("5,000", html);
        Assert.Contains("Subscribe to Basic", html);
        Assert.Equal(0, billing.Calls);
        if (retry)
        {
            await vm.RetryAsync();
            Assert.Equal("Setting up", vm.StateLabel);
            Assert.Equal(1, workspace.Retries);
        }
    }

    [Fact]
    public async Task Checkout_uses_canonical_tenant_origin_and_self_hosting_never_provisions()
    {
        var workspace = new Workspace();
        var billing = new Billing();
        using (var services = Services(workspace, billing))
        {
            await services.GetRequiredService<CloudWorkspaceViewModel>().CheckoutAsync("standard");
            Assert.Equal("https://test.placecontext.io/", billing.Origin!.AbsoluteUri);
            Assert.Equal("standard", billing.Plan);
            Assert.Equal("https://checkout.stripe.com/test", services.GetRequiredService<Navigation>().Destination);
        }
        using (var services = Services(workspace, billing, managed: false))
        {
            var vm = services.GetRequiredService<CloudWorkspaceViewModel>();
            await vm.LoadAsync();
            await vm.RetryAsync();
            await vm.CheckoutAsync("basic");
            Assert.False(vm.Enabled);
            Assert.Null(vm.Usage);
            Assert.Equal(1, billing.Calls);
            Assert.Equal(0, workspace.Retries);
        }
    }

    [Fact]
    public async Task Subscription_redirect_does_not_grant_execution_and_raw_setup_errors_are_not_exposed()
    {
        var workspace = new Workspace { Fail = true };
        using var services = Services(workspace, new Billing());
        var vm = services.GetRequiredService<CloudWorkspaceViewModel>();
        await vm.LoadAsync();
        Assert.NotNull(vm.Error);
        Assert.DoesNotContain("secret", vm.Error);
        Assert.False(vm.Busy);
        workspace.Fail = false;
        await vm.LoadAsync();
        Assert.False(vm.Usage!.CanRun);
        Assert.Null(vm.Error);
    }

    private static ServiceProvider Services(Workspace workspace, Billing billing, bool managed = true, RunAllowanceSnapshot? usage = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ICloudWorkspace>(workspace);
        services.AddSingleton<IRunAllowance>(new Allowance(usage));
        services.AddSingleton<IRunBillingPortal>(billing);
        services.AddSingleton<Navigation>();
        services.AddSingleton(sp => new CloudWorkspaceViewModel(workspace,
            sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new RunBillingDeploymentOptions { ManagedHosting = managed }),
            new FakeCurrentTenant(Guid.NewGuid()), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PlaceContext:PublicBaseUrl"] = "https://placecontext.io" }).Build(),
            sp.GetRequiredService<Navigation>(), NullLogger<CloudWorkspaceViewModel>.Instance));
        return services.BuildServiceProvider();
    }

    private sealed class Navigation : NavigationManager
    {
        public string? Destination;
        public Navigation() => Initialize("https://untrusted.invalid/", "https://untrusted.invalid/cloud?checkout=success");
        protected override void NavigateToCore(string uri, bool forceLoad) => Destination = uri;
    }
    private sealed class Workspace : ICloudWorkspace
    {
        public CloudWorkspaceStatus Status = new("pending", "basic", 0, null);
        public int Retries;
        public bool Fail;
        public Task<CloudWorkspaceStatus?> GetAsync(CancellationToken ct = default) => Fail
            ? throw new InvalidOperationException("Internal secret") : Task.FromResult<CloudWorkspaceStatus?>(Status);
        public Task RetryAsync(CancellationToken ct = default)
        {
            Retries++;
            Status = new("pending", "basic", 0, null);
            return Task.CompletedTask;
        }
    }
    private sealed class Allowance(RunAllowanceSnapshot? usage) : IRunAllowance
    {
        public Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(
            usage ?? new RunAllowanceSnapshot("free", "self_hosted_only", 0, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29), null));
        public Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default) => throw new InvalidOperationException("Rendering must never execute jobs.");
    }
    private sealed class Billing : IRunBillingPortal
    {
        public Uri? EnterpriseSalesUrl => null;
        public int Calls;
        public Uri? Origin;
        public string? Plan;
        public Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default)
        {
            Calls++; Origin = returnBase; Plan = plan;
            return Task.FromResult(new Uri("https://checkout.stripe.com/test"));
        }
        public Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default) => CheckoutAsync("portal", returnBase, ct);
    }
}
