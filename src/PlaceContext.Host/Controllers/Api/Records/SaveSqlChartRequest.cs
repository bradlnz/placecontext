namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SaveSqlChartRequest(string Name, string Sql, string ChartType);
