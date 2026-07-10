using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>Create a table in a project's database from a validated column spec.</summary>
public sealed record CreateProjectTableCommand(Guid ProjectId, string TableName, IReadOnlyList<ProjectColumnSpec> Columns) : ICommand<bool>;
