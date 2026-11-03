using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class BillingSettingsViewModel(
    IRunAllowance allowance, IRunBillingPortal portal, NavigationManager navigation,
    PortalUiState ui, IOptions<RunBillingDeploymentOptions> deployment) : PageViewModel
{
    public RunAllowanceSnapshot? Current { get; private set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }
    public string? StartPlan { get; private set; }
    public bool Welcome { get; private set; }
    public string? CheckoutNotice { get; private set; }
    public bool ManagedHosting => deployment.Value.ManagedHosting;
    public bool HasActiveSubscription => Current is { Plan: not RunPlans.Developer, Status: "active" or "trialing" };
    public bool CanManage => Current?.HasBillingAccount == true;
    public bool CanChoosePlan => ManagedHosting && Current is not null && !HasActiveSubscription && !Busy;
    public bool SubscriptionRequired => Current?.Status == "self_hosted_only";
    public bool RunsUnavailable => Current is { CanRun: false };
    public string SelectedPlanName => StartPlan is null ? "" : RunPlans.NameFor(StartPlan);
    public string PlanName => SubscriptionRequired ? "Subscription required" : RunPlans.NameFor(Current?.Plan ?? RunPlans.Developer);
    public string StatusLabel => Current?.Status switch
    {
        "active" => "Active",
        "trialing" => "Trial",
        "past_due" => "Payment overdue",
        "canceled" => "Canceled",
        "unpaid" => "Unpaid",
        "self_hosted_only" => "Choose a managed plan",
        "configuration_required" => "Configuration required",
        "incomplete" or "incomplete_expired" => "Payment incomplete",
        _ => "Unavailable",
    };
    public string UsageLabel => $"{Current?.UsedRuns ?? 0:N0} top-level starts";
    public string PeriodLabel => Current is { } plan
        ? $"{plan.PeriodStart:yyyy-MM-dd} to {plan.PeriodEnd:yyyy-MM-dd} (UTC)"
        : "Unavailable";
    public string StartRateLabel => Current?.StartsPerMinute is { } rate
        ? $"Up to {rate:N0} workflow/job starts per minute"
        : "No application-imposed start pacing";
    public Uri? EnterpriseSalesUrl => portal.EnterpriseSalesUrl;

    public void SetParameters(string? startPlan, string? welcome, string? checkout)
    {
        StartPlan = startPlan is RunPlans.Basic or RunPlans.Standard ? startPlan : null;
        Welcome = welcome == "1";
        CheckoutNotice = checkout switch
        {
            "success" => "Checkout returned successfully. Your plan updates after payment confirmation; refresh if it is still pending.",
            "cancel" => "Checkout was canceled. No plan change has been confirmed.",
            _ => null,
        };
    }

    public async Task LoadAsync()
    {
        if (Busy)
        {
            return;
        }
        ui.Set("Billing & usage", "Workspace subscription, payments and usage");
        Busy = true;
        Error = null;
        Current = null;
        NotifyStateChanged();
        try
        {
            Current = await allowance.GetAsync();
        }
        catch
        {
            Error = "Could not load billing information. Try refreshing.";
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public Task CheckoutAsync(string plan)
    {
        if (!CanChoosePlan || plan is not (RunPlans.Basic or RunPlans.Standard))
        {
            return Task.CompletedTask;
        }
        return NavigateAsync(() => portal.CheckoutAsync(plan, Origin()));
    }

    public Task ManageAsync()
    {
        if (!CanManage)
        {
            return Task.CompletedTask;
        }
        return NavigateAsync(() => portal.PortalAsync(Origin()));
    }

    private Uri Origin()
    {
        var url = new Uri(navigation.BaseUri);
        return new Uri(url.GetLeftPart(UriPartial.Authority));
    }

    private async Task NavigateAsync(Func<Task<Uri>> start)
    {
        if (Busy)
        {
            return;
        }
        Busy = true;
        Error = null;
        NotifyStateChanged();
        try
        {
            navigation.NavigateTo((await start()).ToString(), forceLoad: true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }
}
