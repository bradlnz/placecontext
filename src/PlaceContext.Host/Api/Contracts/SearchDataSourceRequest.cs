namespace PlaceContext.Host.Api;

public sealed record SearchDataSourceRequest(string Query, int Take = 10);
