using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>One artifact the OCR daemon should process, with everything it needs to fetch and report back.</summary>
public sealed record PendingOcrArtifactView(
    Guid Id,
    Guid RunId,
    Guid JobId,
    string Title,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string DownloadUrl);

/// <summary>
/// The oldest artifacts still awaiting OCR (image/PDF/text types), oldest first, capped at
/// <c>Take</c>. Requires <see cref="Permission.ArtifactsView"/>.
/// </summary>
public sealed record ListPendingOcrQuery(int Take = 10)
    : IQuery<IReadOnlyList<PendingOcrArtifactView>>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsView;
}
