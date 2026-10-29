using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// The oldest artifacts still awaiting OCR (image/PDF/text types), oldest first, capped at
/// <c>Take</c>. Requires <see cref="Permission.ArtifactsView"/>.
/// </summary>
public sealed record ListPendingOcrQuery(int Take = 10)
    : IQuery<IReadOnlyList<PendingOcrArtifactView>>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsView;
}
