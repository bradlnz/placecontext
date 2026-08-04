using Microsoft.AspNetCore.Components;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class RoutesViewModel(NavigationManager navigation)
    : PageViewModel,
        IComponentViewModel,
        IDisposable
{
    public void NavigateToLocked() => navigation.NavigateTo("/locked", forceLoad: true);

    public void Dispose() => Detach();
}
