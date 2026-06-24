using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfChangeLedgerRepository : IChangeLedgerRepository
{
    private readonly AppDbContext _db;
    public EfChangeLedgerRepository(AppDbContext db) => _db = db;

    public async Task<ChangeLedger> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.ChangeRecords.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderBy(x => x.Sequence).ToListAsync(ct);

        var records = rows.Select(r => ChangeRecord.Rehydrate(
            ChangeRecordId.From(r.Id), r.Sequence, r.Summary,
            Author.From(r.AuthorName, Enum.Parse<ActorKind>(r.AuthorKind)),
            Rationale.OrNone(r.Rationale),
            TestDelta.From(r.TestsAdded, r.TestsRemoved, r.TestsChanged),
            DebtDelta.From(r.DebtResolved, r.DebtIntroduced),
            new ChangeVerification(r.ArchReviewed, r.LiveVerified),
            JsonCodec.DecodeStrings(r.TouchedFiles),
            JsonCodec.DecodeStrings(r.TouchedNodes).Select(GraphNodeId.From),
            r.CommitSha is null ? null : CommitSha.From(r.CommitSha),
            r.RecordedAt));

        return ChangeLedger.Rehydrate(projectId, records);
    }

    public async Task SaveAsync(ChangeLedger ledger, CancellationToken ct = default)
    {
        // Append-only: insert new records, otherwise the only mutation is a commit sha attach.
        foreach (var r in ledger.Records)
        {
            var row = await _db.ChangeRecords.FindAsync(new object[] { r.Id.Value }, ct);
            if (row is null)
                await _db.ChangeRecords.AddAsync(ToRow(ledger.ProjectId, r), ct);
            else
                row.CommitSha = r.Commit?.Value;
        }
    }

    private static ChangeRecordRow ToRow(ProjectId pid, ChangeRecord r) => new()
    {
        Id = r.Id.Value,
        ProjectId = pid.Value,
        Sequence = r.Sequence,
        Summary = r.Summary,
        AuthorName = r.Author.Name,
        AuthorKind = r.Author.Kind.ToString(),
        Rationale = r.Rationale.IsPresent ? r.Rationale.Value : "",
        TestsAdded = r.TestDelta.Added,
        TestsRemoved = r.TestDelta.Removed,
        TestsChanged = r.TestDelta.Changed,
        DebtResolved = r.DebtDelta.Resolved,
        DebtIntroduced = r.DebtDelta.Introduced,
        ArchReviewed = r.Verification.ArchitectureReviewerRun,
        LiveVerified = r.Verification.LiveVerified,
        TouchedFiles = JsonCodec.Encode(r.TouchedFiles),
        TouchedNodes = JsonCodec.Encode(r.TouchedNodes.Select(n => n.Value).ToList()),
        CommitSha = r.Commit?.Value,
        RecordedAt = r.RecordedAt
    };
}
