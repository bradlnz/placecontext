namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EntityRecordUpdatePageRequest(
    IReadOnlyDictionary<string, string?> Keys,
    IReadOnlyDictionary<string, string?> Values);
