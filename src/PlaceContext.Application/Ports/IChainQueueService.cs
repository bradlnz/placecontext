using System.Text.Json;

namespace PlaceContext.Application.Ports;

public sealed record ChainQueueDefinition(Guid Id, Guid ProjectId, Guid ChainId, string Name, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int MessageTtlSeconds = 86400, int ReceiptRetentionDays = 30, int MaxAttempts = 3, int RetryDelaySeconds = 3, int DeliveryDelaySeconds = 0);

public interface IChainQueueService
{
    Task<IReadOnlyList<ChainQueueDefinition>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<ChainQueueDefinition> SaveAsync(Guid? id, Guid projectId, Guid chainId, string name, bool enabled, int messageTtlSeconds = 86400, int receiptRetentionDays = 30, int maxAttempts = 3, int retryDelaySeconds = 3, int deliveryDelaySeconds = 0, CancellationToken ct = default);
    Task RetryAsync(Guid queueId, Guid trackingId, CancellationToken ct = default);
    Task DeleteAsync(Guid queueId, CancellationToken ct = default);
    Task<JobChainSubmission> SubmitAsync(Guid queueId, JsonElement payload, string? idempotencyKey, DateTimeOffset? executeAt = null, CancellationToken ct = default);
    Task<IReadOnlyList<JobChainSubmission>> ListMessagesAsync(Guid queueId, CancellationToken ct = default);
    Task<JobChainSubmission?> GetMessageAsync(Guid queueId, Guid trackingId, CancellationToken ct = default);
}
