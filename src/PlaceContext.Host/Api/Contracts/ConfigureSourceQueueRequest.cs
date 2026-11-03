namespace PlaceContext.Host.Api;

public sealed record ConfigureSourceQueueRequest(Guid QueueId, string[] RequiredFields, string[] RedactedFields, bool TrimText = true);
