using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Integration;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

/// <summary>
/// Writes OCR extraction results into a project's <c>ocr_results</c> system table — the same
/// read-only, platform-owned append pattern as <see cref="DataMappingIngestionService"/>: the table
/// is created on first write with provenance columns (<c>ingested_at</c>, <c>artifact_id</c>,
/// <c>run_id</c>, <c>job_id</c>), text cells are encrypted at rest, and the project role only ever
/// gets <c>SELECT</c>. One row per artifact per completion (the tracking columns on
/// <c>job_run_artifacts</c> decide whether a new row is written).
/// </summary>
public sealed class OcrResultStorageService
{
    private readonly IArtifactDataClient _data;
    private readonly IClock _clock;
    private readonly ILogger<OcrResultStorageService>? _log;

    public OcrResultStorageService(IArtifactDataClient data, IClock clock, ILogger<OcrResultStorageService>? log = null)
    {
        _data = data;
        _clock = clock;
        _log = log;
    }

    /// <summary>Appends one OCR row for <paramref name="link"/> into its project's <c>ocr_results</c> table.</summary>
    public async Task StoreAsync(
        RunArtifactLink link,
        string markdown,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;

        await _data.StoreOcrResultAsync(link, markdown, _clock.UtcNow, ct);
        _log?.LogInformation("Stored OCR result for artifact {ArtifactId} (run {RunId}).", link.Id, link.RunId);
    }
}
