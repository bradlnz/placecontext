using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface IMcpConnectionRepository
{
    Task<IReadOnlyList<McpConnection>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<McpConnection?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(McpConnection connection, CancellationToken ct = default);
    Task UpdateAsync(McpConnection connection, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
