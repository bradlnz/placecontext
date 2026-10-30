namespace PlaceContext.Application.Ports;

public sealed record ProjectTableSchema(string Name, IReadOnlyList<ProjectColumnInfo> Columns);
