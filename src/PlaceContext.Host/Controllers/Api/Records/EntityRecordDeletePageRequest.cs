namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EntityRecordDeletePageRequest(
    IReadOnlyDictionary<string, string?> Keys);
