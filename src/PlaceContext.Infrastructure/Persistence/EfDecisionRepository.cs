using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfDecisionRepository : IDecisionRepository
{
    private readonly AppDbContext _db;
    public EfDecisionRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Decision d, CancellationToken ct = default)
        => await _db.Decisions.AddAsync(new DecisionRow
        {
            Id = d.Id.Value, ProjectId = d.ProjectId.Value, Question = d.Question, Choice = d.Choice,
            Rationale = d.Rationale.IsPresent ? d.Rationale.Value : "", DecidedAt = d.DecidedAt
        }, ct);

    public async Task<IReadOnlyList<Decision>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.Decisions.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderByDescending(x => x.DecidedAt).ToListAsync(ct);

        return rows.Select(r => Decision.Rehydrate(
            DecisionId.From(r.Id), projectId, r.Question, r.Choice,
            Rationale.OrNone(r.Rationale), Array.Empty<DecisionId>(), r.DecidedAt)).ToList();
    }
}
