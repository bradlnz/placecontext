using System.Text;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// The materialization outcome: the created index, how many rows were copied, and whether the
/// table held more rows than the export cap (rows beyond it were not indexed).
/// </summary>
public sealed record MaterializeTableIndexResult(
    string IndexName,
    long RowsIndexed,
    int ColumnCount,
    bool Truncated,
    string SourceTable);

/// <summary>
/// Copy a project-owned Postgres table's rows into an OpenSearch index (mapping derived from the
/// table's column types) so the data can be searched and aggregated alongside collected indices.
/// Materializing over an existing index replaces it — the index is deleted and recreated first.
/// </summary>
public sealed record MaterializeTableIndexCommand(
    Guid ProjectId,
    string TableName,
    string? IndexName = null) : ICommand<MaterializeTableIndexResult>
{
    /// <summary>Default index name for a table: the <c>pi-</c> prefix plus the table name.</summary>
    public static string DefaultIndexName(string tableName)
    {
        var sb = new StringBuilder("pi-");
        foreach (var ch in (tableName ?? "").Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        var name = sb.ToString().Trim('-');
        if (name.Length is <= 3 or > 255) name = "pi-materialized";
        return name;
    }

    /// <summary>Postgres column type → OpenSearch mapping type. Long <c>format_type</c> names
    /// (e.g. <c>timestamp with time zone</c>) are accepted alongside the short UI names.</summary>
    public static string OpenSearchTypeFor(string postgresType)
    {
        var t = (postgresType ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "integer" or "int" or "serial" => "integer",
            "bigint" or "bigserial" or "long" => "long",
            "numeric" or "decimal" or "double precision" or "double" or "float" or "real" => "double",
            "boolean" or "bool" => "boolean",
            "timestamptz" or "timestamp" or "timestamp with time zone" or "timestamp without time zone" or "date" => "date",
            "uuid" => "keyword",
            "jsonb" or "json" => "object",
            _ => "text",
        };
    }
}
