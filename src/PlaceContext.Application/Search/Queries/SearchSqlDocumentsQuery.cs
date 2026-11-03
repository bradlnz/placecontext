using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.ProjectData;
namespace PlaceContext.Application.Features;

public sealed record SearchSqlDocumentsQuery(Guid ProjectId, string Sql)
    : IQuery<ProjectQueryResult>;
