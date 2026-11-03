namespace PlaceContext.Host.Api;

public sealed record ProjectDataTableResponse(string Name, long EstimatedRows, bool IsView, bool ReadOnly);
