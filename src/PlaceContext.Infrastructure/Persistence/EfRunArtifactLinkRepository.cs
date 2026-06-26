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
    };

    private static RunArtifactLink ToDomain(RunArtifactLinkRow r) => RunArtifactLink.Rehydrate(
        r.Id, r.RunId, r.JobId, r.ProjectId,
        Enum.TryParse<PostJobActionKind>(r.Kind, out var k) ? k : PostJobActionKind.HtmlReport,
        r.Title, r.Bucket, r.ObjectKey, r.ContentType, r.SizeBytes, r.CreatedAt);
}
