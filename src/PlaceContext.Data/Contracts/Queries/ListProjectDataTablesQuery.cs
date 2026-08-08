using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>List the tables in a project's own database.</summary>
public sealed record ListProjectDataTablesQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectTableInfo>>;
