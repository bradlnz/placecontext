using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmJobRunRepository : ICrmJobRunRepository
{
    private readonly AppDbContext _db;

    public EfCrmJobRunRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CrmJobRun run, CancellationToken ct = default)
        => await _db.CrmJobRuns.AddAsync(new CrmJobRunRow
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            ClientId = run.ClientId,
            JobId = run.JobId,
            RunId = run.RunId,
            LifecycleStage = run.LifecycleStage.ToString(),
            StartedAt = run.StartedAt,
        }, ct);

    public async Task<IReadOnlyList<CrmJobRun>> ListForClientAsync(
        Guid clientId,
        int take = 20,
        CancellationToken ct = default)
        => (await _db.CrmJobRuns.AsNoTracking()
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct))
            .Select(row => new CrmJobRun(
                row.Id,
                row.ProjectId,
                row.ClientId,
                row.JobId,
                row.RunId,
                Enum.TryParse<CustomerLifecycleStage>(row.LifecycleStage, out var stage)
                    ? stage
                    : CustomerLifecycleStage.Lead,
                row.StartedAt))
            .ToList();
}
