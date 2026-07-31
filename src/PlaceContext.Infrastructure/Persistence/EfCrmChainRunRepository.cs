using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmChainRunRepository : ICrmChainRunRepository
{
    private readonly AppDbContext _db;

    public EfCrmChainRunRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CrmChainRun run, CancellationToken ct = default)
        => await _db.CrmChainRuns.AddAsync(new CrmChainRunRow
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            ClientId = run.ClientId,
            ChainId = run.ChainId,
            ChainRunId = run.ChainRunId,
            LifecycleStage = run.LifecycleStage.ToString(),
            StartedAt = run.StartedAt,
        }, ct);

    public async Task<IReadOnlyList<CrmChainRun>> ListForClientAsync(
        Guid clientId,
        int take = 20,
        CancellationToken ct = default)
        => (await _db.CrmChainRuns.AsNoTracking()
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct))
            .Select(row => new CrmChainRun(
                row.Id,
                row.ProjectId,
                row.ClientId,
                row.ChainId,
                row.ChainRunId,
                Enum.TryParse<CustomerLifecycleStage>(row.LifecycleStage, out var stage)
                    ? stage : CustomerLifecycleStage.Lead,
                row.StartedAt))
            .ToList();
}
