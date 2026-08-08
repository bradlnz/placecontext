namespace PlaceContext.Domain.Repositories;

/// <summary>Commits one AgentChat persistence transaction.</summary>
public interface IAgentChatUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
