using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Starts a portal background operation with tenant resolution.
/// Shared by Jobs and JobChains run launchers (SRP: tenancy + OperationCenter only).
/// </summary>
public sealed class BackgroundOperationRunner
{
    private readonly OperationCenter _opCenter;

    public BackgroundOperationRunner(OperationCenter opCenter) => _opCenter = opCenter;

    /// <summary>
    /// Queue work on the operation center. Returns null on success, or an error message
    /// when no tenant is resolved.
    /// </summary>
    public string? TryRun(
        Guid projectId,
        string title,
        string href,
        Func<IServiceProvider, CancellationToken, Task<string?>> work,
        string? correlationKey = null)
    {
        var tenant = CurrentTenant.Current;
        if (tenant is null)
            return "No tenant resolved — sign in again.";

        _opCenter.Run(tenant, projectId, title, href, work, correlationKey: correlationKey);
        return null;
    }
}
