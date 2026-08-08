using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record SearchOpenSearchSqlQuery(Guid ProjectId, string Sql)
    : IQuery<ProjectQueryResult>;
