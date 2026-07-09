using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Inbound SMS messages (ciphertext at rest), newest-first reads.</summary>
public interface IInboundSmsRepository
{
    Task AddAsync(SmsMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<SmsMessage>> ListRecentAsync(int take = 50, CancellationToken ct = default);
    /// <summary>Whether a message with this provider id was already stored (webhook retries).</summary>
    Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct = default);
}
