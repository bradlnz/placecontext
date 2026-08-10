using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class LoginViewModel(IAuthService? auth = null, NavigationManager? navigation = null) : PageViewModel
{
    public string? Error { get; private set; }
    public string? ReturnUrl { get; private set; }
    public string? Email { get; private set; }

    public void SetParameters(string? error, string? returnUrl, string? email)
    {
        Error = error;
        ReturnUrl = returnUrl;
        Email = email;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (auth is not null && navigation is not null && await auth.IsUnconfiguredAsync(cancellationToken))
        {
            var target = string.IsNullOrWhiteSpace(ReturnUrl)
                ? "/setup"
                : $"/setup?returnUrl={Uri.EscapeDataString(ReturnUrl)}";
            navigation.NavigateTo(target, forceLoad: true, replace: true);
        }
    }
}
