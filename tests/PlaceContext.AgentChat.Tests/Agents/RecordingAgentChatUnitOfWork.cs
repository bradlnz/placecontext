using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Tests.Agents;

public sealed class RecordingAgentChatUnitOfWork : IAgentChatUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(SaveCount);
    }
}
