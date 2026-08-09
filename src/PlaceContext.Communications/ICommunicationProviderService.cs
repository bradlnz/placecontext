using PlaceContext.Communications.Contracts;

namespace PlaceContext.Communications;

public interface ICommunicationProviderService
{
    Task<IReadOnlyList<CommunicationProviderView>> ListAsync(CancellationToken ct = default);
    Task<CommunicationProviderView?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CommunicationProviderView> CreateAsync(CommunicationProviderInput input, CancellationToken ct = default);
    Task<CommunicationProviderView> UpdateAsync(Guid id, CommunicationProviderInput input, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<CommunicationProviderView> SetDefaultAsync(Guid id, CancellationToken ct = default);
    Task<CommunicationProviderView> SetTwoFactorAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task<IReadOnlyList<string>> TwoFactorChannelsAsync(CancellationToken ct = default);
    Task<ResolvedCommunicationProvider?> ResolveForSendAsync(string channel, CancellationToken ct = default);
    Task<ResolvedCommunicationProvider?> ResolveForTwoFactorAsync(string channel, CancellationToken ct = default);
    Task<ResolvedCommunicationProvider> ResolveByIdAsync(Guid id, CancellationToken ct = default);
}
