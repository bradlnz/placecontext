using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class SetupViewModel(
    IAuthService auth, NavigationManager navigation,
    IOptions<RunBillingDeploymentOptions>? hosting = null) : PageViewModel
{
    public bool Configured { get; private set; }
    public string? Error { get; set; }
    public string? ReturnUrl { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }

    public void SetParameters(string? error, string? returnUrl, string? email, string? displayName)
    {
        Error = error;
        ReturnUrl = returnUrl;
        Email = email;
        DisplayName = displayName;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Managed tenant owners must be created by verified platform signup,
        // never by the self-hosted first-run bootstrap.
        if (hosting?.Value.ManagedHosting == true)
        {
            navigation.NavigateTo(PageRoutes.Login, forceLoad: true, replace: true);
            return;
        }
        Configured = !await auth.IsUnconfiguredAsync(cancellationToken);
        if (Configured)
            navigation.NavigateTo(PageRoutes.Login, forceLoad: true);
    }
}
