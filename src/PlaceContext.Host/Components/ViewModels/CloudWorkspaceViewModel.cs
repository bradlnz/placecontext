using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Host.Tenancy;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class CloudWorkspaceViewModel(ICloudWorkspace workspace, IServiceScopeFactory scopes,
    IOptions<RunBillingDeploymentOptions> options, ICurrentTenant tenant, IConfiguration configuration,
    NavigationManager navigation, ILogger<CloudWorkspaceViewModel> logger, PortalUiState? ui = null) : PageViewModel
{
    public bool Enabled => options.Value.ManagedHosting;
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public CloudWorkspaceStatus? Workspace { get; private set; }
    public RunAllowanceSnapshot? Usage { get; private set; }
    public string StateLabel => Workspace?.State switch
    {
        "ready" => "Ready",
        "failed" => "Setup failed",
        "pending" or "provisioning" => "Setting up",
        _ => "Existing workspace"
    };
    public bool CanRetry => Workspace is { State: "failed", Attempts: >= 5 };
    public bool NeedsRefresh => Workspace is { State: "pending" or "provisioning" }
        or { State: "failed", RetryAt: not null } || Usage?.SubscriptionActive != true;
    public int RunLimit(string plan) => plan == RunPlans.Standard
        ? options.Value.StandardRunsPerPeriod : options.Value.BasicRunsPerPeriod;
    public int AiLimit(string plan) => plan == RunPlans.Standard
        ? options.Value.StandardAiRequestsPerPeriod : options.Value.BasicAiRequestsPerPeriod;

    public Task LoadAsync() => PerformAsync(async () =>
    {
        ui?.Set("Cloud workspace", "setup, usage and subscription");
        Workspace = await workspace.GetAsync();
        await using var scope = scopes.CreateAsyncScope();
        Usage = await scope.ServiceProvider.GetRequiredService<IRunAllowance>().GetAsync();
    });

    public Task RetryAsync() => PerformAsync(async () =>
    {
        await workspace.RetryAsync();
        Workspace = await workspace.GetAsync();
    });

    public Task CheckoutAsync(string plan) => BillingAsync(plan);
    public Task ManageAsync() => BillingAsync(null);
    private Task BillingAsync(string? plan) => PerformAsync(async () =>
    {
        await using var scope = scopes.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<IRunBillingPortal>();
        var origin = ManagedSignupUrls.TenantUri(configuration, tenant.Slug);
        var url = plan is null ? await billing.PortalAsync(origin) : await billing.CheckoutAsync(plan, origin);
        navigation.NavigateTo(url.AbsoluteUri, forceLoad: true);
    });

    private async Task PerformAsync(Func<Task> action)
    {
        if (!Enabled || Busy) return;
        Busy = true;
        Error = null;
        NotifyStateChanged();
        try { await action(); }
        catch (UnauthorizedAccessException) { Error = "Workspace administration is required."; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cloud workspace operation failed.");
            Error = "We couldn't complete that request. Refresh or try again shortly.";
        }
        finally { Busy = false; NotifyStateChanged(); }
    }
}
