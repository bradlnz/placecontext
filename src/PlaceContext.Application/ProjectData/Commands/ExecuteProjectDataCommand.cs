using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>Run SQL inside a project's own database (a command — SQL may write).</summary>
public sealed record ExecuteProjectDataCommand(Guid ProjectId, string Sql) : ICommand<ProjectQueryResult>;
