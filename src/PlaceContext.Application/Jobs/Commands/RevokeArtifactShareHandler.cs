using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class RevokeArtifactShareHandler : ICommandHandler<RevokeArtifactShareCommand, bool>
{
    private readonly IArtifactShareTokenService _shares;

    public RevokeArtifactShareHandler(IArtifactShareTokenService shares) => _shares = shares;

    public Task<bool> HandleAsync(RevokeArtifactShareCommand command, CancellationToken ct = default)
        => _shares.RevokeAsync(command.ArtifactId, ct);
}
