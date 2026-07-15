using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfDecisionRepository : IDecisionRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => IDataEncryptor.Purpose.Decision;

    public EfDecisionRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task AddAsync(Decision d, CancellationToken ct = default)
        => await _db.Decisions.AddAsync(new DecisionRow
        {
            Id = d.Id.Value,
            ProjectId = d.ProjectId.Value,
            Question = _enc.Protect(d.Question, P),
            Choice = _enc.Protect(d.Choice, P),
            Rationale = d.Rationale.IsPresent ? _enc.Protect(d.Rationale.Value, P) : "",
            DecidedAt = d.DecidedAt
        }, ct);

    public async Task<IReadOnlyList<Decision>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.Decisions.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderByDescending(x => x.DecidedAt).ToListAsync(ct);

        return rows.Select(r => Decision.Rehydrate(
            DecisionId.From(r.Id), projectId,
            _enc.Unprotect(r.Question, P),
            _enc.Unprotect(r.Choice, P),
            Rationale.OrNone(_enc.Unprotect(r.Rationale, P)),
            Array.Empty<DecisionId>(), r.DecidedAt)).ToList();
    }
}
