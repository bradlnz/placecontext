namespace PlaceContext.Application.DataSources;

public sealed record SourceReview(string Status, double Confidence, string Model, string PolicyFingerprint, DateTimeOffset ReviewedAt);
