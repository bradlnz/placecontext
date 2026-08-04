namespace PlaceContext.Host.Components.ViewModels;

/// <summary>
/// Lightweight base class for page ViewModels. Holds a dispatcher-safe <c>StateHasChanged</c>
/// callback so the ViewModel can trigger a Blazor re-render without depending on
/// any Blazor types — keeping it testable and framework-agnostic.
///
/// Usage in a razor file:
///   @inject MyPageViewModel Vm
///   @code {
///       protected override void OnInitialized() => Vm.Attach(() => InvokeAsync(StateHasChanged));
///       public void Dispose() => Vm.Detach();
///   }
/// </summary>
public abstract class PageViewModel
{
    public PagePresentationCatalog Presentation { get; } = new();

    public bool TryArtifactSvg(string? artifact, out string svg) =>
        ArtifactChart.TrySvg(artifact, out svg);

    private Func<Task>? _stateHasChanged;

    /// <summary>Wire the component's <c>StateHasChanged</c> into the ViewModel.</summary>
    /// <remarks>
    /// Pass a Func<Task> that marshals to the Blazor dispatcher, e.g.
    /// <c>() => InvokeAsync(StateHasChanged)</c>.
    /// </remarks>
    public void Attach(Func<Task> stateHasChanged) => _stateHasChanged = stateHasChanged;

    /// <summary>Detach when the component is disposed.</summary>
    public void Detach() => _stateHasChanged = null;

    /// <summary>
    /// Call from any property setter or method that mutates visible state.
    /// The callback is invoked asynchronously and exceptions are observed so
    /// background updates never crash the caller or leak unobserved tasks.
    /// </summary>
    protected void NotifyStateChanged()
    {
        var callback = _stateHasChanged;
        if (callback is null)
            return;
        _ = SafeNotifyAsync(callback);
    }

    private static async Task SafeNotifyAsync(Func<Task> callback)
    {
        try
        {
            await callback();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NotifyStateChanged failed: {ex}");
        }
    }
}
