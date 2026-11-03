namespace PlaceContext.Application.DataSources;

public sealed record SourceRecord(string Key, string Json, SourceReview? Review = null, string? ValidationError = null);
