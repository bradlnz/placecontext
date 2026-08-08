namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EntityRecordCreatePageRequest(
    IReadOnlyDictionary<string, string?> Values);
