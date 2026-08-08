using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Domain.Persistence;

namespace PlaceContext.TestSupport;

public sealed class RecordingUnitOfWork : IUnitOfWork, IDataUnitOfWork, IJobsUnitOfWork
{
    public int SaveCount { get; private set; }
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(SaveCount);
    }
}
