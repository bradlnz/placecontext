namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record QueueAnalyticsRefreshRequest(string? TableName, string? Instruction);
