namespace PlaceContext.Application.DataSources;

public sealed record SourceProcessingPolicy(string Criteria, double MinimumConfidence = 0.9,
    Guid? ApprovedBy = null, DateTimeOffset? ApprovedAt = null);
