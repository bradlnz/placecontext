using Microsoft.AspNetCore.Components;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class OnboardingViewModel(NavigationManager navigation) : PageViewModel
{
    public void Initialize() => navigation.NavigateTo(PageRoutes.GettingStartedWiki, replace: true);
}
