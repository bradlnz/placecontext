using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfAgentConfigRepository : IAgentConfigRepository
{
    private readonly AppDbContext _db;

    public EfAgentConfigRepository(AppDbContext db) => _db = db;

    public async Task<AgentConfig?> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var row = await _db.AgentConfigs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task AddAsync(AgentConfig config, CancellationToken ct = default)
        => await _db.AgentConfigs.AddAsync(ToRow(config), ct);

    public async Task UpdateAsync(AgentConfig config, CancellationToken ct = default)
    {
        var existing = await _db.AgentConfigs.FindAsync(new object[] { config.Id }, ct);
        if (existing is null) return;

        var updated = ToRow(config);
        existing.BaseModel = updated.BaseModel;
        existing.SystemPrompt = updated.SystemPrompt;
        existing.MaxContextChunks = updated.MaxContextChunks;
        existing.Temperature = updated.Temperature;
        existing.TopP = updated.TopP;
        existing.Enabled = updated.Enabled;
        existing.UpdatedAt = updated.UpdatedAt;
    }

    private static AgentConfigRow ToRow(AgentConfig c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        BaseModel = c.BaseModel,
        SystemPrompt = c.SystemPrompt,
        MaxContextChunks = c.MaxContextChunks,
        Temperature = c.Temperature,
        TopP = c.TopP,
        Enabled = c.Enabled,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static AgentConfig ToDomain(AgentConfigRow r) => AgentConfig.Rehydrate(
        r.Id, r.ProjectId, r.BaseModel, r.SystemPrompt,
        r.MaxContextChunks, r.Temperature, r.TopP, r.Enabled,
        r.CreatedAt, r.UpdatedAt);
}
