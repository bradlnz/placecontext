namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EntityRecordLinksPageRequest(
    IReadOnlyDictionary<string, string?> Values);
