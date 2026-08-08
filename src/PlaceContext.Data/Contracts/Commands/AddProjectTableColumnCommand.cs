using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>Add a column to an existing table in a project's database.</summary>
public sealed record AddProjectTableColumnCommand(Guid ProjectId, string TableName, ProjectColumnSpec Column) : ICommand<bool>;
