namespace PlaceContext.Host.Components.ViewModels;

/// <summary>
/// Lightweight base class for page ViewModels. Holds a <see cref="StateHasChanged"/>
/// callback so the ViewModel can trigger a Blazor re-render without depending on
/// any Blazor types — keeping it testable and framework-agnostic.
///
/// Usage in a razor file:
///   @inject MyPageViewModel Vm
///   @code {
///       protected override void OnInitialized() => Vm.Attach(StateHasChanged);
///       public void Dispose() => Vm.Detach();
///   }
/// </summary>
public abstract class PageViewModel
{
    private Action? _stateHasChanged;

    /// <summary>Wire the component's <c>StateHasChanged</c> into the ViewModel.</summary>
    public void Attach(Action stateHasChanged) => _stateHasChanged = stateHasChanged;

    /// <summary>Detach when the component is disposed.</summary>
    public void Detach() => _stateHasChanged = null;

    /// <summary>Call from any property setter or method that mutates visible state.</summary>
    protected void NotifyStateChanged() => _stateHasChanged?.Invoke();
}
