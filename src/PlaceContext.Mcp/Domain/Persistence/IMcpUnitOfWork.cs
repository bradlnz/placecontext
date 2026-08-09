namespace PlaceContext.Domain.Repositories;

/// <summary>Commits one MCP persistence transaction.</summary>
public interface IMcpUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
