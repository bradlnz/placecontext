using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;
namespace PlaceContext.Application.Features;

public sealed record ListProjectTableSchemasQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectTableSchema>>;
