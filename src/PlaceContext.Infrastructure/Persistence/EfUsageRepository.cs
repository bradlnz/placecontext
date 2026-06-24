using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfUsageRepository : IUsageRepository
{
    private readonly AppDbContext _db;
    public EfUsageRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(UsageRecord record, CancellationToken ct = default)
        => await _db.UsageRecords.AddAsync(new UsageRow
        {
            Id = record.Id,
            ProjectId = record.ProjectId.Value,
            Model = record.Usage.Model,
            InputTokens = record.Usage.InputTokens,
            OutputTokens = record.Usage.OutputTokens,
            Description = record.Description,
            RecordedAt = record.RecordedAt,
        }, ct);

    public async Task<IReadOnlyList<UsageRecord>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var rows = await _db.UsageRecords.AsNoTracking()
            .Where(x => x.ProjectId == projectId.Value).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<UsageRecord>> ListAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.UsageRecords.AsNoTracking().ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static UsageRecord ToDomain(UsageRow r) => UsageRecord.Rehydrate(
        r.Id, ProjectId.From(r.ProjectId), TokenUsage.From(r.Model, r.InputTokens, r.OutputTokens), r.Description, r.RecordedAt);
}
