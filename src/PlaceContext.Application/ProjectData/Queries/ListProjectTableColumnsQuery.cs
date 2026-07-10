using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>List the columns of one table in a project's database.</summary>
public sealed record ListProjectTableColumnsQuery(Guid ProjectId, string TableName) : IQuery<IReadOnlyList<ProjectColumnInfo>>;
