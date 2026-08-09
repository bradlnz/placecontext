namespace PlaceContext.Data.Contracts.Api;

public sealed record QueueAnalyticsRefreshRequest(string? TableName, string? Instruction);
