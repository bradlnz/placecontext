using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryChatCommandRepository : IChatCommandRepository
{
    private readonly List<ChatCommand> _store = new();

    public Task AddAsync(ChatCommand command, CancellationToken ct = default)
    {
        _store.Add(command);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ChatCommand command, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveAsync(Guid commandId, CancellationToken ct = default)
    {
        _store.RemoveAll(c => c.Id == commandId);
        return Task.CompletedTask;
    }

    public Task<ChatCommand?> GetByIdAsync(Guid commandId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(c => c.Id == commandId));

    public Task<IReadOnlyList<ChatCommand>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChatCommand>>(
            _store.Where(c => c.ProjectId == projectId).ToList());
}
