using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

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
