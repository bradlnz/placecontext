using PlaceContext.Application.Shared;

namespace PlaceContext.TestSupport;

public sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int SaveCount
    {
        get; private set;
    }
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(SaveCount);
    }
}
