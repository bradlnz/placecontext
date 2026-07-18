using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfActivityLogRepository : IActivityLogRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => IDataEncryptor.Purpose.Activity;

    public EfActivityLogRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task<ActivityLog> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.ActivityRecords.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderBy(x => x.Sequence).ToListAsync(ct);

        var records = rows.Select(r => ActivityRecord.Rehydrate(
            ActivityRecordId.From(r.Id), r.Sequence,
            _enc.Unprotect(r.Summary, P),
            Author.From(r.AuthorName, Enum.Parse<ActorKind>(r.AuthorKind)),
            Rationale.OrNone(_enc.Unprotect(r.Rationale, P)),
            TestDelta.From(r.TestsAdded, r.TestsRemoved, r.TestsChanged),
            new ActivityVerification(r.ArchReviewed, r.LiveVerified),
            JsonCodec.DecodeStrings(r.TouchedFiles),
            JsonCodec.DecodeStrings(r.TouchedNodes).Select(GraphNodeId.From),
            r.CommitSha is null ? null : CommitSha.From(r.CommitSha),
            r.RecordedAt));

        return ActivityLog.Rehydrate(projectId, records);
    }

    public async Task SaveAsync(ActivityLog ledger, CancellationToken ct = default)
    {
        // Append-only: insert new records, otherwise the only mutation is a commit sha attach.
        foreach (var r in ledger.Records)
        {
            var row = await _db.ActivityRecords.FindAsync(new object[] { r.Id.Value }, ct);
            if (row is null)
                await _db.ActivityRecords.AddAsync(ToRow(ledger.ProjectId, r), ct);
            else
                row.CommitSha = r.Commit?.Value;
        }
    }

    private ActivityRecordRow ToRow(ProjectId pid, ActivityRecord r) => new()
    {
        Id = r.Id.Value,
        ProjectId = pid.Value,
        Sequence = r.Sequence,
        Summary = _enc.Protect(r.Summary, P),
        AuthorName = r.Author.Name,
        AuthorKind = r.Author.Kind.ToString(),
        Rationale = r.Rationale.IsPresent ? _enc.Protect(r.Rationale.Value, P) : "",
        TestsAdded = r.TestDelta.Added,
        TestsRemoved = r.TestDelta.Removed,
        TestsChanged = r.TestDelta.Changed,
        ArchReviewed = r.Verification.ArchitectureReviewerRun,
        LiveVerified = r.Verification.LiveVerified,
        TouchedFiles = JsonCodec.Encode(r.TouchedFiles),
        TouchedNodes = JsonCodec.Encode(r.TouchedNodes.Select(n => n.Value).ToList()),
        CommitSha = r.Commit?.Value,
        RecordedAt = r.RecordedAt
    };
}
