namespace PlaceContext.Application.Ports;

/// <summary>One table in a project's own database. Read-only tables are system-written (e.g.
/// job run results): the project can SELECT them but not modify, rename, or drop them.</summary>
public sealed record ProjectTableInfo(string Name, long RowEstimate, bool ReadOnly = false, bool IsView = false);
