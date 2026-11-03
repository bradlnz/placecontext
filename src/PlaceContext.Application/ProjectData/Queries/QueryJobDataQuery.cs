using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.Features;

public sealed record QueryJobDataQuery(string Sql) : IQuery<ProjectQueryResult>;
