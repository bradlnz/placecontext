using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfRiskAssessmentRepository : IRiskAssessmentRepository
{
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _enc;
    private static string P => IDataEncryptor.Purpose.Risk;

    public EfRiskAssessmentRepository(AppDbContext db, IDataEncryptor enc) => (_db, _enc) = (db, enc);

    public async Task AddAsync(RiskAssessment a, CancellationToken ct = default)
        => await _db.RiskAssessments.AddAsync(new RiskAssessmentRow
        {
            Id = a.Id.Value, ProjectId = a.ProjectId.Value,
            Technical = a.Technical.Value, Process = a.Process.Value,
            Signals = _enc.Protect(JsonCodec.EncodeSignals(a.Signals), P),
            ComputedAt = a.ComputedAt
        }, ct);

    public async Task<RiskAssessment?> GetLatestAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var r = await _db.RiskAssessments.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync(ct);
        return r is null ? null : ToDomain(projectId, r);
    }

    public async Task<IReadOnlyList<RiskAssessment>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.RiskAssessments.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).OrderBy(x => x.ComputedAt).ToListAsync(ct);
        return rows.Select(r => ToDomain(projectId, r)).ToList();
    }

    private RiskAssessment ToDomain(ProjectId pid, RiskAssessmentRow r) => RiskAssessment.Rehydrate(
        AssessmentId.From(r.Id), pid, RiskScore.From(r.Technical), RiskScore.From(r.Process),
        JsonCodec.DecodeSignals(_enc.Unprotect(r.Signals, P)), r.ComputedAt);
}
