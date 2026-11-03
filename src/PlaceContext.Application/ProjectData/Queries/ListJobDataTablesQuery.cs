using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.Features;

public sealed record ListJobDataTablesQuery : IQuery<IReadOnlyList<ProjectTableInfo>>;
