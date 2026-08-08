using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

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
