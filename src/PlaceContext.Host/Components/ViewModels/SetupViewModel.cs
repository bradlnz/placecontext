using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class SetupViewModel(IAuthService auth, NavigationManager navigation) : PageViewModel
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
        Configured = !await auth.IsUnconfiguredAsync(cancellationToken);
        if (Configured)
            navigation.NavigateTo(PageRoutes.Login, forceLoad: true);
    }
}
