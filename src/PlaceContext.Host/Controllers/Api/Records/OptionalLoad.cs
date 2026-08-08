namespace PlaceContext.Host.Controllers.Api.Records;

internal sealed record OptionalLoad<T>(T? Value, string? Error) where T : class;
