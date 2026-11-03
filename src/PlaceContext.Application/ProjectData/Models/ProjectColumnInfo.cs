namespace PlaceContext.Application.ProjectData;

/// <summary>One existing column of a project table, as the database reports it.</summary>
public sealed record ProjectColumnInfo(string Name, string Type, bool NotNull, bool PrimaryKey);
