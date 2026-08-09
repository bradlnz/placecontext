using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class EfCrmChainRunRepository : ICrmChainRunRepository
{
    private readonly CrmDbContext _db;

    public EfCrmChainRunRepository(CrmDbContext db) => _db = db;

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

    public async Task<CrmChainRun?> GetByChainRunIdAsync(
        Guid chainRunId,
        CancellationToken ct = default)
    {
        var row = await _db.CrmChainRuns.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ChainRunId == chainRunId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<CrmChainRun>> ListForClientAsync(
        Guid clientId,
        int take = 20,
        CancellationToken ct = default)
        => (await _db.CrmChainRuns.AsNoTracking()
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct))
            .Select(Map)
            .ToList();

    private static CrmChainRun Map(CrmChainRunRow row) => new(
        row.Id,
        row.ProjectId,
        row.ClientId,
        row.ChainId,
        row.ChainRunId,
        Enum.TryParse<CustomerLifecycleStage>(row.LifecycleStage, out var stage)
            ? stage : CustomerLifecycleStage.Lead,
        row.StartedAt);
}
