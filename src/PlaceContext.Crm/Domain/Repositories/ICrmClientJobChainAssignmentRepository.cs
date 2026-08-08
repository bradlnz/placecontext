namespace PlaceContext.Domain.Repositories;

public interface ICrmClientJobChainAssignmentRepository
{
    Task<IReadOnlyList<Guid>> ListForClientAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken ct = default);

    Task SetForClientAsync(
        Guid projectId,
        Guid clientId,
        IReadOnlyList<Guid> chainIds,
        CancellationToken ct = default);
}
