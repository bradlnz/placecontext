using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Dtos;
public sealed record QueryDataChainActionView(string Sql, string OutputField = "query_data")
    : ChainActionView(QueryDataChainAction.ActionType, "Query data");
