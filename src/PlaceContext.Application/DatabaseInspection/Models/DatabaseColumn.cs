namespace PlaceContext.Application.DatabaseInspection;

public sealed record DatabaseColumn(string Name, string Type, bool Nullable, bool PrimaryKey, bool Redacted);
