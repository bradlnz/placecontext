using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.DatabaseInspection;

public sealed record QueryDatabaseQuery(string Sql) : IQuery<ProjectQueryResult>;
