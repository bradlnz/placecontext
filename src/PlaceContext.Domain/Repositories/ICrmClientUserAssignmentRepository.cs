namespace PlaceContext.Domain.Repositories;

public interface ICrmClientUserAssignmentRepository
{
    Task<IReadOnlyList<Guid>> ListForCrmUserAsync(
        Guid projectId,
        Guid crmUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListForClientAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken ct = default);

    Task SetForClientAsync(
        Guid projectId,
        Guid clientId,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default);
}
