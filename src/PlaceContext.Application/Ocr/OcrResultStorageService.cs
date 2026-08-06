using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
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
    private static readonly IReadOnlyList<ProjectColumnSpec> Columns = new[]
    {
        new ProjectColumnSpec("ingested_at", DataColumnTypes.Timestamptz, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("artifact_id", DataColumnTypes.Uuid, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("run_id", DataColumnTypes.Uuid, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("job_id", DataColumnTypes.Uuid, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("title", DataColumnTypes.Text, NotNull: false, PrimaryKey: false),
        new ProjectColumnSpec("content_type", DataColumnTypes.Text, NotNull: false, PrimaryKey: false),
        new ProjectColumnSpec("markdown", DataColumnTypes.Text, NotNull: true, PrimaryKey: false),
    };

    private readonly IProjectDataStore _store;
    private readonly IClock _clock;
    private readonly ILogger<OcrResultStorageService>? _log;

    public OcrResultStorageService(IProjectDataStore store, IClock clock, ILogger<OcrResultStorageService>? log = null)
    {
        _store = store;
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

        var row = new string?[]
        {
            _clock.UtcNow.ToString("O"),
            link.Id.ToString(),
            link.RunId.ToString(),
            link.JobId.ToString(),
            link.Title,
            link.ContentType,
            markdown,
        };
        await _store.AppendReadOnlyRowsAsync(link.ProjectId, "ocr_results", Columns, new[] { row }, ct);
        _log?.LogInformation("Stored OCR result for artifact {ArtifactId} (run {RunId}).", link.Id, link.RunId);
    }
}
