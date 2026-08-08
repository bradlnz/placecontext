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
