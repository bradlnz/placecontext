using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record CreateArtifactShareCommand(Guid ArtifactId, int LifetimeDays = 7)
    : ICommand<ArtifactShareCreated>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}

public sealed class CreateArtifactShareHandler : ICommandHandler<CreateArtifactShareCommand, ArtifactShareCreated>
{
    private readonly IArtifactShareTokenService _shares;
    private readonly ICurrentUser _user;

    public CreateArtifactShareHandler(IArtifactShareTokenService shares, ICurrentUser user)
        => (_shares, _user) = (shares, user);

    public Task<ArtifactShareCreated> HandleAsync(
        CreateArtifactShareCommand command,
        CancellationToken ct = default)
        => _shares.CreateOrRotateAsync(command.ArtifactId, _user.UserId, command.LifetimeDays, ct);
}

public sealed record RevokeArtifactShareCommand(Guid ArtifactId)
    : ICommand<bool>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}

public sealed class RevokeArtifactShareHandler : ICommandHandler<RevokeArtifactShareCommand, bool>
{
    private readonly IArtifactShareTokenService _shares;

    public RevokeArtifactShareHandler(IArtifactShareTokenService shares) => _shares = shares;

    public Task<bool> HandleAsync(RevokeArtifactShareCommand command, CancellationToken ct = default)
        => _shares.RevokeAsync(command.ArtifactId, ct);
}

public sealed record GetArtifactShareStatusQuery(Guid ArtifactId)
    : IQuery<ArtifactShareStatus?>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}

public sealed class GetArtifactShareStatusHandler
    : IQueryHandler<GetArtifactShareStatusQuery, ArtifactShareStatus?>
{
    private readonly IArtifactShareTokenService _shares;

    public GetArtifactShareStatusHandler(IArtifactShareTokenService shares) => _shares = shares;

    public Task<ArtifactShareStatus?> HandleAsync(
        GetArtifactShareStatusQuery query,
        CancellationToken ct = default)
        => _shares.GetStatusAsync(query.ArtifactId, ct);
}
