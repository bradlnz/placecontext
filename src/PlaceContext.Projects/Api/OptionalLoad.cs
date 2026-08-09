namespace PlaceContext.Projects.Api;

internal sealed record OptionalLoad<T>(T? Value, string? Error) where T : class;
