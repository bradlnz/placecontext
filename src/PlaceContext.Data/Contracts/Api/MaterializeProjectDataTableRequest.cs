namespace PlaceContext.Data.Contracts.Api;

public sealed record MaterializeProjectDataTableRequest(string TableName, string? IndexName);
