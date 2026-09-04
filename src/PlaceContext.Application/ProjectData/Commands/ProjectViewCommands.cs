using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates or replaces a named VIEW in the project's database from a SELECT (joins across the
/// project's tables). Guarded twice: the definition must be a single SELECT-only statement, and
/// the DDL executes as the project's own Postgres role inside its schema — so a view can only read
/// this project's tables, and data edits remain possible only on tables the project defines
/// (system tables stay read-only at the Postgres level).
/// </summary>
public sealed record SaveProjectViewCommand(Guid ProjectId, string Name, string SelectSql) : ICommand<bool>;

/// <summary>Drops a view from the project's schema.</summary>
public sealed record DropProjectViewCommand(Guid ProjectId, string Name) : ICommand<bool>;
