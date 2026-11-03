namespace PlaceContext.Application.DataSources;

public sealed record SourceQueueConfiguration(Guid QueueId, IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> RedactedFields, bool TrimText = true, Guid? ApprovedBy = null);
