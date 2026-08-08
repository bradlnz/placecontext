using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmCommunicationRepository
{
    Task AddAsync(CrmCommunication communication, CancellationToken ct = default);
    Task UpdateAsync(CrmCommunication communication, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCommunication>> ListForClientAsync(
        Guid clientId,
        int take = 100,
        CancellationToken ct = default);
}
