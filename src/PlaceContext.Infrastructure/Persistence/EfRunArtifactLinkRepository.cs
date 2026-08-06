using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfRunArtifactLinkRepository : IRunArtifactLinkRepository
{
    private readonly AppDbContext _db;
    public EfRunArtifactLinkRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RunArtifactLink link, CancellationToken ct = default)
        => await _db.RunArtifacts.AddAsync(ToRow(link), ct);

    public async Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(Guid runId, CancellationToken ct = default)
    {
        var rows = await _db.RunArtifacts.AsNoTracking()
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.RunArtifacts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var rows = await _db.RunArtifacts.AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(200) // the run-history panel shows recent runs; no need to hydrate a job's whole life
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(int take, CancellationToken ct = default)
    {
        var rows = await _db.RunArtifacts.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 5000))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(Guid projectId, int take, string? search = null, CancellationToken ct = default)
    {
        var query = _db.RunArtifacts.AsNoTracking().Where(r => r.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // EF translates Contains to ILIKE/LOWER LIKE against Postgres — parameterized, never
            // string-concatenated into SQL.
            query = query.Where(r => EF.Functions.ILike(r.Title, $"%{search}%") || EF.Functions.ILike(r.Kind, $"%{search}%"));
        }
        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 20000))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<RunArtifactLink>> ListPendingOcrAsync(int take, CancellationToken ct = default)
    {
        // Oldest-first so a freshly-deployed daemon drains the backlog in arrival order, and only
        // content types the daemon can actually process. text/* is included so plain-text artifacts
        // flow through the same pipeline even though no OCR is applied to them.
        var rows = await _db.RunArtifacts.AsNoTracking()
            .Where(r => r.OcrProcessedAt == null)
            .Where(r => r.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || r.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                || r.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task MarkOcrProcessedAsync(Guid artifactId, DateTimeOffset processedAt, string? error, CancellationToken ct = default)
    {
        // Query (not FindAsync) so the tenant global filter applies — a caller can only mark an
        // artifact that belongs to its own workspace.
        var row = await _db.RunArtifacts.FirstOrDefaultAsync(r => r.Id == artifactId, ct);
        if (row is null) return;
        row.OcrProcessedAt = processedAt;
        row.OcrError = string.IsNullOrWhiteSpace(error) ? null : error;
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        // Query (not FindAsync) so the tenant global filter applies — a caller can only delete
        // an artifact that belongs to its own workspace.
        var row = await _db.RunArtifacts.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is not null) _db.RunArtifacts.Remove(row);
    }

    private static RunArtifactLinkRow ToRow(RunArtifactLink l) => new()
    {
        Id = l.Id,
        RunId = l.RunId,
        JobId = l.JobId,
        ProjectId = l.ProjectId,
        Kind = l.Kind.ToString(),
        Title = l.Title,
        Bucket = l.Bucket,
        ObjectKey = l.ObjectKey,
        ContentType = l.ContentType,
        SizeBytes = l.SizeBytes,
        CreatedAt = l.CreatedAt,
        OcrProcessedAt = l.OcrProcessedAt,
        OcrError = l.OcrError,
    };

    private static RunArtifactLink ToDomain(RunArtifactLinkRow r) => RunArtifactLink.Rehydrate(
        r.Id, r.RunId, r.JobId, r.ProjectId,
        Enum.TryParse<PostJobActionKind>(r.Kind, out var k) ? k : PostJobActionKind.HtmlReport,
        r.Title, r.Bucket, r.ObjectKey, r.ContentType, r.SizeBytes, r.CreatedAt,
        r.OcrProcessedAt, r.OcrError);
}
