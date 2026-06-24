using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>In-memory token-usage store.</summary>
public sealed class InMemoryUsageRepository : IUsageRepository
{
    private readonly List<UsageRecord> _records = new();

    public Task AddAsync(UsageRecord record, CancellationToken ct = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageRecord>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsageRecord>>(_records.Where(r => r.ProjectId == projectId).ToList());

    public Task<IReadOnlyList<UsageRecord>> ListAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UsageRecord>>(_records.ToList());
}
