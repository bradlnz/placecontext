namespace PlaceContext.Application.Ports;

/// <summary>One column in a create-table request. Type is a Postgres type chosen from a safe allow-list.</summary>
public sealed record ProjectColumnSpec(string Name, string Type, bool NotNull, bool PrimaryKey);
