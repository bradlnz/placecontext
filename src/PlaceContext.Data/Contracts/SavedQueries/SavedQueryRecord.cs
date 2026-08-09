namespace PlaceContext.Application.Ports;

/// <summary>A named, project-scoped SQL query the user saved from the SQL Studio editor.</summary>
public sealed record SavedQueryRecord(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Sql,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
