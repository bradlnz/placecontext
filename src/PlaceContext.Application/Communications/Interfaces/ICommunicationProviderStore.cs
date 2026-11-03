namespace PlaceContext.Application.Communications;

public interface ICommunicationProviderStore
{
    Task<IReadOnlyList<CommunicationProviderConfiguration>> ListAsync(CancellationToken ct = default);
    Task<CommunicationProviderConfiguration?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(CommunicationProviderConfiguration provider, CancellationToken ct = default);
    Task UpdateAsync(CommunicationProviderConfiguration provider, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
