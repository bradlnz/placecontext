using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>Admin-only subscription view. Reads consumption from the durable run ledger;
/// checkout and portal navigation use Stripe-hosted pages.</summary>
public sealed class BillingSettingsViewModel(
    IRunAllowance allowance, IRunBillingPortal portal, NavigationManager navigation,
    IOptions<StripeRunBillingOptions> settings, PortalUiState ui) : PageViewModel
{
    public RunAllowanceSnapshot? Current { get; private set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }

    public Uri? EnterpriseSalesUrl
    {
        get
        {
            return Uri.TryCreate(settings.Value.EnterpriseSalesUrl, UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps ? url : null;
        }
    }

    public async Task LoadAsync()
    {
        ui.Set("Billing", "Run allowances and subscription");
        Busy = true;
        try { Current = await allowance.GetAsync(); Error = null; }
        catch { Error = "Could not load billing information."; }
        finally { Busy = false; NotifyStateChanged(); }
    }

    public Task CheckoutAsync(string plan) => NavigateAsync(() => portal.CheckoutAsync(plan, Origin()));
    public Task ManageAsync() => NavigateAsync(() => portal.PortalAsync(Origin()));

    private Uri Origin()
    {
        var url = new Uri(navigation.BaseUri);
        return new Uri(url.GetLeftPart(UriPartial.Authority));
    }

    private async Task NavigateAsync(Func<Task<Uri>> start)
    {
        if (Busy) return;
        Busy = true; Error = null; NotifyStateChanged();
        try { navigation.NavigateTo((await start()).ToString(), forceLoad: true); }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; NotifyStateChanged(); }
    }
}
