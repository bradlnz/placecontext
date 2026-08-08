using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Runtime;

/// <summary>Async-flow caller context used by independently hosted services.</summary>
public sealed class ServiceCurrentUser : ICurrentUser, ICurrentUserAccessor
{
    private readonly AsyncLocal<UserContext?> _current = new();

    public Guid UserId => _current.Value?.Id ?? Guid.Empty;
    public string Role => _current.Value?.Role ?? nameof(UserRole.Viewer);
    public bool IsAuthenticated => _current.Value is not null;

    public void Set(UserContext user) => _current.Value = user;

    public void Clear() => _current.Value = null;
}
