using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class EfCrmAutomationRuleRepository : ICrmAutomationRuleRepository
{
    private readonly AppDbContext _db;

    public EfCrmAutomationRuleRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CrmAutomationRule rule, CancellationToken ct = default)
        => await _db.CrmAutomationRules.AddAsync(ToRow(rule), ct);

    public async Task UpdateAsync(CrmAutomationRule rule, CancellationToken ct = default)
    {
        var row = await _db.CrmAutomationRules.FirstOrDefaultAsync(x => x.Id == rule.Id, ct);
        if (row is null) return;
        row.Name = rule.Name;
        row.EventType = rule.EventType.ToString();
        row.LifecycleStage = rule.LifecycleStage?.ToString();
        row.ChainId = rule.ChainId;
        row.Enabled = rule.Enabled;
        row.UpdatedAt = rule.UpdatedAt;
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmAutomationRules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is not null) _db.CrmAutomationRules.Remove(row);
    }

    public async Task<CrmAutomationRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmAutomationRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<CrmAutomationRule>> ListForProjectAsync(
        Guid projectId, CancellationToken ct = default)
        => (await _db.CrmAutomationRules.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Enabled)
            .ThenBy(x => x.EventType)
            .ThenBy(x => x.LifecycleStage)
            .ThenBy(x => x.Name)
            .ToListAsync(ct))
            .Select(ToDomain).ToList();

    public async Task<IReadOnlyList<CrmAutomationRule>> ListMatchingAsync(
        Guid projectId, CrmAutomationEventType eventType, CustomerLifecycleStage? stage,
        CancellationToken ct = default)
        => (await _db.CrmAutomationRules.AsNoTracking()
            .Where(x => x.ProjectId == projectId
                && x.Enabled
                && x.EventType == eventType.ToString()
                && (stage == null
                    ? x.LifecycleStage == null
                    : x.LifecycleStage == null || x.LifecycleStage == stage.ToString()))
            .ToListAsync(ct))
            .Select(ToDomain).ToList();

    private static CrmAutomationRuleRow ToRow(CrmAutomationRule value) => new()
    {
        Id = value.Id,
        ProjectId = value.ProjectId,
        Name = value.Name,
        EventType = value.EventType.ToString(),
        LifecycleStage = value.LifecycleStage?.ToString(),
        ChainId = value.ChainId,
        Enabled = value.Enabled,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt,
    };

    private static CrmAutomationRule ToDomain(CrmAutomationRuleRow row)
        => CrmAutomationRule.Rehydrate(
            row.Id, row.ProjectId, row.Name,
            Enum.TryParse<CrmAutomationEventType>(row.EventType, out var eventType)
                ? eventType : CrmAutomationEventType.StageEntered,
            Enum.TryParse<CustomerLifecycleStage>(row.LifecycleStage, out var stage) ? stage : null,
            row.ChainId, row.Enabled, row.CreatedAt, row.UpdatedAt);
}
