namespace PlaceContext.Application.Shared;

/// <summary>Allowed project-data column types (Postgres-oriented) for UI wizards and data maps.</summary>
public static class DataColumnTypes
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "text", "integer", "bigint", "numeric", "boolean",
        "timestamptz", "date", "uuid", "jsonb",
    };

    public const string Text = "text";
    public const string Integer = "integer";
    public const string Bigint = "bigint";
    public const string Numeric = "numeric";
    public const string Boolean = "boolean";
    public const string Timestamptz = "timestamptz";
    public const string Date = "date";
    public const string Uuid = "uuid";
    public const string Jsonb = "jsonb";
}
