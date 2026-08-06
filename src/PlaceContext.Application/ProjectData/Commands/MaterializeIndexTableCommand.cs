using System.Text;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// The materialization outcome: the created table, how many documents were copied, and whether the
/// index had more documents than the export cap (rows beyond it were not imported).
/// </summary>
public sealed record MaterializeIndexResult(
    string TableName,
    int RowsImported,
    int ColumnCount,
    bool Truncated,
    string SourceIndex);

/// <summary>
/// Copy an OpenSearch index's documents into a project-owned Postgres table (created from the
/// index's field schema) so the data can be joined with project tables in a single SQL statement.
/// Materializing over an existing project table replaces it; a read-only system table or view with
/// the same name is refused.
/// </summary>
public sealed record MaterializeIndexTableCommand(
    Guid ProjectId,
    string IndexPattern,
    string? TableName = null) : ICommand<MaterializeIndexResult>
{
    /// <summary>Default Postgres table name for an index: lower-case, non-alphanumerics → underscores.</summary>
    public static string DefaultTableName(string indexPattern)
    {
        var sb = new StringBuilder();
        foreach (var ch in (indexPattern ?? "").Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        var name = sb.ToString().Trim('_');
        if (name.Length == 0) name = "materialized";
        if (!char.IsLetter(name[0])) name = "idx_" + name;
        return name.Length > 63 ? name[..63] : name;
    }

    /// <summary>Valid Postgres column identifier for an OpenSearch field (dots, @, dashes → underscores).</summary>
    public static string ColumnName(string rawField)
    {
        var sb = new StringBuilder();
        foreach (var ch in (rawField ?? "").Trim())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        var name = sb.ToString().Trim('_');
        if (name.Length == 0) name = "field";
        if (!char.IsLetter(name[0])) name = "f_" + name;
        return name.Length > 63 ? name[..63] : name;
    }
}
