namespace PlaceContext.Application.ProjectData;

public sealed record ProjectTableSchema(string Name, IReadOnlyList<ProjectColumnInfo> Columns);
