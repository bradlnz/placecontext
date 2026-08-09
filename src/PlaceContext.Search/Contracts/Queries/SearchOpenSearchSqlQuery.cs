using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record SearchOpenSearchSqlQuery(Guid ProjectId, string Sql)
    : IQuery<OpenSearchSqlResult>;
