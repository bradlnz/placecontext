namespace PlaceContext.Agents.Domain.Persistence;

public interface IAgentsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
