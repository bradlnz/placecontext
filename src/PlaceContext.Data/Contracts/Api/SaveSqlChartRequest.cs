namespace PlaceContext.Data.Contracts.Api;

public sealed record SaveSqlChartRequest(string Name, string Sql, string ChartType);
