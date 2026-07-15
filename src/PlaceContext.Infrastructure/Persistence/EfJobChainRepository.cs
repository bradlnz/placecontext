using System.Text.Json;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
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
        existing.StagesJson = SerializeStages(chain.Stages);
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
        StagesJson = SerializeStages(c.Stages),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static string SerializeStages(IReadOnlyList<ChainStage> stages)
        => JsonSerializer.Serialize(stages.Select(s => s.JobIds));

    private static JobChain ToDomain(JobChainRow r) => JobChain.Rehydrate(
        r.Id, r.ProjectId, r.Name, r.Description, DeserializeStages(r.StagesJson), r.CreatedAt, r.UpdatedAt);

    /// <summary>
    /// Reads the stages JSON array-of-arrays. Tolerates a legacy flat array of job ids too (rows
    /// written before fan-out existed, or a not-yet-migrated <c>StepJobIdsJson</c> value carried
    /// over verbatim) by treating each id as its own size-1 stage.
    /// </summary>
    private static List<ChainStage> DeserializeStages(string stagesJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stagesJson) ? "[]" : stagesJson);
        var root = doc.RootElement;
        var stages = new List<ChainStage>();
        foreach (var element in root.EnumerateArray())
        {
            stages.Add(element.ValueKind == JsonValueKind.Array
                ? new ChainStage(element.EnumerateArray().Select(e => e.GetGuid()))
                : ChainStage.Of(element.GetGuid())); // legacy flat shape: one job id per element
        }
        return stages;
    }
}
