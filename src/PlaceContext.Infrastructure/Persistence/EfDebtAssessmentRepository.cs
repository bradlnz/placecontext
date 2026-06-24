using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfDebtAssessmentRepository : IDebtAssessmentRepository
{
    private readonly AppDbContext _db;
    public EfDebtAssessmentRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(DebtAssessment a, CancellationToken ct = default)
        => await _db.DebtAssessments.AddAsync(new DebtAssessmentRow
        {
            Id = a.Id.Value, ProjectId = a.ProjectId.Value,
            Technical = a.Technical.Value, Agentic = a.Agentic.Value,
            Signals = JsonCodec.EncodeSignals(a.Signals), ComputedAt = a.ComputedAt
        }, ct);

    public async Task<DebtAssessment?> GetLatestAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var r = await _db.DebtAssessments.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync(ct);
        return r is null ? null : ToDomain(projectId, r);
    }

    public async Task<IReadOnlyList<DebtAssessment>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.DebtAssessments.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderBy(x => x.ComputedAt).ToListAsync(ct);
        return rows.Select(r => ToDomain(projectId, r)).ToList();
    }

    private static DebtAssessment ToDomain(ProjectId pid, DebtAssessmentRow r) => DebtAssessment.Rehydrate(
        AssessmentId.From(r.Id), pid, DebtScore.From(r.Technical), DebtScore.From(r.Agentic),
        JsonCodec.DecodeSignals(r.Signals), r.ComputedAt);
}
