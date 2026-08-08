using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface IChatCommandRepository
{
    Task AddAsync(ChatCommand command, CancellationToken ct = default);
    Task UpdateAsync(ChatCommand command, CancellationToken ct = default);
    Task RemoveAsync(Guid commandId, CancellationToken ct = default);
    Task<ChatCommand?> GetByIdAsync(Guid commandId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatCommand>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
