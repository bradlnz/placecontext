using System.Text.Json;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfJobChainRepository : IJobChainRepository
{
    private readonly AppDbContext _db;

    public EfJobChainRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(JobChain chain, CancellationToken ct = default)
        => await _db.JobChains.AddAsync(ToRow(chain), ct);

    public async Task UpdateAsync(JobChain chain, CancellationToken ct = default)
    {
        var existing = await _db.JobChains.FindAsync(new object[] { chain.Id }, ct);
        if (existing is null) return;

        existing.Name = chain.Name;
        existing.Description = chain.Description;
        existing.StepJobIdsJson = JsonSerializer.Serialize(chain.StepJobIds);
        existing.UpdatedAt = chain.UpdatedAt;
    }

    public async Task RemoveAsync(Guid chainId, CancellationToken ct = default)
    {
        var existing = await _db.JobChains.FindAsync(new object[] { chainId }, ct);
        if (existing is not null) _db.JobChains.Remove(existing);
    }

    public async Task<JobChain?> GetByIdAsync(Guid chainId, CancellationToken ct = default)
    {
        var row = await _db.JobChains.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chainId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<JobChain>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.JobChains.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static JobChainRow ToRow(JobChain c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        Name = c.Name,
        Description = c.Description,
        StepJobIdsJson = JsonSerializer.Serialize(c.StepJobIds),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static JobChain ToDomain(JobChainRow r) => JobChain.Rehydrate(
        r.Id, r.ProjectId, r.Name, r.Description,
        JsonSerializer.Deserialize<List<Guid>>(r.StepJobIdsJson) ?? new List<Guid>(),
        r.CreatedAt, r.UpdatedAt);
}
