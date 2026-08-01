using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>
/// Ambient (AsyncLocal) holder for the current caller's identity + role, mirroring
/// <see cref="CurrentTenant"/> — registered as a singleton but reading a value that flows with the
/// async execution context, so it is correct inside the dispatcher's per-operation child DI scopes. Set
/// once per request by the Host's <c>UserResolutionMiddleware</c> (after authorization has resolved the
/// scheme-correct principal) and re-applied per Blazor circuit activity by <c>UserCircuitHandler</c>.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private static readonly AsyncLocal<UserIdentity?> _current = new();

    /// <summary>The identity captured in the current async flow (used by the Blazor circuit handler to re-apply it).</summary>
    public static UserIdentity? Current => _current.Value;

    public static void Set(UserIdentity user) => _current.Value = user;
    public static void Clear() => _current.Value = null;

    public Guid UserId => _current.Value?.Id ?? Guid.Empty;
    public string Role => _current.Value?.Role ?? nameof(UserRole.Viewer);
    public bool IsAuthenticated => _current.Value is not null;
}

/// <summary>The id + role name resolved off a request's authenticated principal.</summary>
public sealed record UserIdentity(Guid Id, string Role);
