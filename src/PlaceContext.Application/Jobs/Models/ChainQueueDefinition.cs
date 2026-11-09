namespace PlaceContext.Application.Jobs;

public sealed record ChainQueueDefinition(Guid Id, Guid ProjectId, Guid ChainId, string Name, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int MessageTtlSeconds = 86400, int ReceiptRetentionDays = 30, int MaxAttempts = 3, int RetryDelaySeconds = 3, int DeliveryDelaySeconds = 0);
