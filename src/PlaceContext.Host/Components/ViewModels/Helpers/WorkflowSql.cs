using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>One click-to-add WHERE condition in the workflow query studio.</summary>
public sealed record QueryDataFilter(string Column, string Type, string? Value);

/// <summary>
/// Builds and describes the read-only SQL used by a workflow's Query data step.
///
/// Safety: every helper takes the project table schema loaded for the current project and only
/// emits canonical table/column names from it. Nothing typed by the user is interpolated as an
/// identifier, and filters are matched back to a real column before their type is used. Preview
/// itself goes through the project-scoped <c>QueryProjectDataAsync</c>, which runs read-only with
/// the project's role and a pinned search_path (see SqlProjectDataStore).
///
/// The studio also keeps generated queries small and bounded so they can be previewed and passed
/// to the next job without hitting the runner's row cap or 1 MiB pipeline limit unexpectedly.
/// </summary>
public static class WorkflowSql
{
    /// <summary>Rows sampled from a project table in the values pane.</summary>
    public const int SampleRows = 25;

    /// <summary>LIMIT written by the toolbar helpers.</summary>
    public const int DefaultRowLimit = 100;

    /// <summary>Mirrors the runner/store row cap; truncated previews stop the next step.</summary>
    public const int MaxRows = 500;

    /// <summary>Mirrors the runner's serialized-pipeline limit.</summary>
    public const int PipelineByteLimit = 1_048_576;

    /// <summary>Shown in the studio so the execution boundary is explicit to the user.</summary>
    public const string IsolationNote =
        "Preview runs read-only against this project's isolated database schema. Only this project's tables are visible.";

    private static readonly Regex IdentifierRe = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.Compiled);

    private static readonly Regex NumericRe = new(
        @"^-?\d+(?:\.\d+)?$", RegexOptions.Compiled);

    private static readonly Regex LimitRe = new(
        @"\blimit\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TableReferenceRe = new(
        @"\b(?:from|join)\s+(?:""([^""]+)""|([A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsValidIdentifier(string name) => IdentifierRe.IsMatch(name);

    /// <summary>Quotes a project identifier. Project tables/columns are created from these names,
    /// so a validated identifier can be interpolated safely into preview SQL.</summary>
    public static string Quote(string identifier)
    {
        if (!IsValidIdentifier(identifier))
        {
            throw new ArgumentException($"'{identifier}' is not a valid SQL identifier.");
        }
        return "\"" + identifier + "\"";
    }

    public static string Literal(string? value) => "'" + (value ?? "").Replace("'", "''") + "'";

    /// <summary>Resolves a column by name against the project schema. Case-insensitive lookup keeps
    /// hand-typed filters friendly, but the emitted identifier always uses the canonical name.</summary>
    public static ProjectColumnInfo Column(ProjectTableSchema table, string name)
    {
        var column = table.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            throw new ArgumentException($"'{name}' is not a column of the project table '{table.Name}'.");
        }
        return column;
    }

    public static string Predicate(ProjectTableSchema table, QueryDataFilter filter)
    {
        var column = Column(table, filter.Column);
        var quoted = Quote(column.Name);
        if (filter.Value is null)
        {
            return $"{quoted} IS NULL";
        }

        var type = column.Type.ToLowerInvariant();
        if (IsNumericType(type) && NumericRe.IsMatch(filter.Value))
        {
            return $"{quoted} = {filter.Value}";
        }
        if (type == "boolean" && bool.TryParse(filter.Value, out var boolean))
        {
            return $"{quoted} = {(boolean ? "TRUE" : "FALSE")}";
        }
        if (type == "uuid" && Guid.TryParse(filter.Value, out var guid))
        {
            return $"{quoted} = '{guid:D}'";
        }
        if (type.StartsWith("json", StringComparison.Ordinal))
        {
            return $"{quoted}::text = {Literal(filter.Value)}";
        }
        return $"{quoted} = {Literal(filter.Value)}";
    }

    private static bool IsNumericType(string type) =>
        type.StartsWith("smallint", StringComparison.Ordinal)
        || type.StartsWith("integer", StringComparison.Ordinal)
        || type.StartsWith("bigint", StringComparison.Ordinal)
        || type.StartsWith("numeric", StringComparison.Ordinal)
        || type.StartsWith("decimal", StringComparison.Ordinal)
        || type.StartsWith("real", StringComparison.Ordinal)
        || type.StartsWith("double", StringComparison.Ordinal);

    public static string BuildSelect(
        ProjectTableSchema table,
        IReadOnlyList<string> columns,
        IReadOnlyList<QueryDataFilter> filters,
        int limit = DefaultRowLimit)
    {
        if (columns.Count == 0)
        {
            throw new ArgumentException("Select at least one column for the query.");
        }

        // Resolve every checked column against the project schema first: a stale checkbox from a
        // previously selected table must never leak an identifier into the emitted SQL.
        var selected = new List<string>();
        foreach (var name in columns)
        {
            var canonical = Column(table, name).Name;
            if (!selected.Contains(canonical, StringComparer.Ordinal))
            {
                selected.Add(canonical);
            }
        }

        var sql = new StringBuilder("SELECT\n");
        for (var i = 0; i < selected.Count; i++)
        {
            sql.Append("    ").Append(Quote(selected[i]));
            sql.Append(i == selected.Count - 1 ? "\n" : ",\n");
        }
        sql.Append("FROM\n    ").Append(Quote(table.Name));
        AppendWhere(sql, table, filters);
        sql.Append("\nLIMIT ").Append(Math.Clamp(limit, 1, MaxRows));
        return sql.ToString();
    }

    public static string BuildCount(ProjectTableSchema table, IReadOnlyList<QueryDataFilter> filters)
    {
        var sql = new StringBuilder("SELECT COUNT(*) AS \"row_count\"\nFROM\n    ").Append(Quote(table.Name));
        AppendWhere(sql, table, filters);
        return sql.ToString();
    }

    private static void AppendWhere(
        StringBuilder sql,
        ProjectTableSchema table,
        IReadOnlyList<QueryDataFilter> filters)
    {
        if (filters.Count == 0)
        {
            return;
        }
        sql.Append("\nWHERE\n    ")
            .Append(string.Join("\n    AND ", filters.Select(filter => Predicate(table, filter))));
    }

    /// <summary>Adds a LIMIT when the editor has none. Returns false when the query already
    /// bounds itself (or is empty), so the caller can leave the editor untouched.</summary>
    public static bool EnsureLimit(string sql, int limit, out string bounded)
    {
        bounded = sql;
        if (string.IsNullOrWhiteSpace(sql) || LimitRe.IsMatch(sql))
        {
            return false;
        }
        bounded = sql.Trim().TrimEnd(';').TrimEnd() + $"\nLIMIT {Math.Clamp(limit, 1, MaxRows)}";
        return true;
    }

    /// <summary>Tables named in FROM/JOIN clauses, in first-reference order, resolved against the
    /// project schema (exact first, then case-insensitive). Unknown tables are ignored — the
    /// isolated database role remains the authority on what can actually be read.</summary>
    public static IReadOnlyList<ProjectTableSchema> ReferencedTables(
        string sql,
        IReadOnlyList<ProjectTableSchema> tables)
    {
        var found = new List<ProjectTableSchema>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TableReferenceRe.Matches(sql))
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var table = tables.FirstOrDefault(t => t.Name == name)
                ?? tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (table is not null && seen.Add(table.Name))
            {
                found.Add(table);
            }
        }
        return found;
    }

    /// <summary>Tables the query names that are not part of this project's schema. The preview
    /// still runs under the isolated project role, but surfacing these early keeps the studio
    /// honest about what it can autocomplete and filter.</summary>
    public static IReadOnlyList<string> UnknownReferencedTables(
        string sql,
        IReadOnlyList<ProjectTableSchema> tables)
    {
        var unknown = new List<string>();
        foreach (Match match in TableReferenceRe.Matches(sql))
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var known = tables.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!known && !unknown.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                unknown.Add(name);
            }
        }
        return unknown;
    }

    /// <summary>A workflow-stopping condition the preview already reveals. The runner refuses
    /// truncated results, duplicate column names, and payloads over 1 MiB.</summary>
    public static string? PreviewWarning(ProjectQueryResult result)
    {
        if (result.Truncated)
        {
            return $"Preview stopped at the workflow's {MaxRows}-row limit. Add a narrower filter or LIMIT; truncated results stop the next step.";
        }
        if (result.Columns.Count == 0)
        {
            return "The query returned no columns. Write a SELECT that returns rows.";
        }
        if (result.Columns.Distinct(StringComparer.Ordinal).Count() != result.Columns.Count)
        {
            return "Column names must be unique. Give duplicate columns SQL aliases; the next step would fail.";
        }
        if (EstimatePipelineBytes(result) > PipelineByteLimit)
        {
            return "The result is over the 1 MiB pipeline limit. Select fewer rows or columns.";
        }
        return null;
    }

    public static string PreviewSummary(ProjectQueryResult result, string outputField)
    {
        if (result.Columns.Count == 0)
        {
            return "No columns returned.";
        }
        var rows = $"{result.Rows.Count} row{(result.Rows.Count == 1 ? "" : "s")}";
        var columns = $"{result.Columns.Count} column{(result.Columns.Count == 1 ? "" : "s")}";
        return $"{rows} · {columns} · saved to {RowsPath(outputField)}";
    }

    public static long EstimatePipelineBytes(ProjectQueryResult result)
    {
        var rows = new JsonArray();
        foreach (var row in result.Rows)
        {
            var item = new JsonObject();
            for (var i = 0; i < result.Columns.Count; i++)
            {
                item[result.Columns[i]] = i < row.Count ? row[i] : null;
            }
            rows.Add(item);
        }
        var envelope = new JsonObject
        {
            ["columns"] = JsonSerializer.SerializeToNode(result.Columns),
            ["rows"] = rows,
            ["row_count"] = result.Rows.Count,
            ["truncated"] = result.Truncated,
        };
        return Encoding.UTF8.GetByteCount(envelope.ToJsonString());
    }

    /// <summary>The first row shaped exactly like the next step receives it, or null when the
    /// result has no rows.</summary>
    public static string? SampleRow(ProjectQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return null;
        }
        var item = new JsonObject();
        for (var i = 0; i < result.Columns.Count; i++)
        {
            var column = result.Columns[i];
            if (item.ContainsKey(column))
            {
                continue;
            }
            item[column] = i < result.Rows[0].Count ? result.Rows[0][i] : null;
        }
        return item.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string RowsPath(string outputField) =>
        string.IsNullOrWhiteSpace(outputField) ? "query_data.rows" : $"{outputField.Trim()}.rows";

    public static string FilterLabel(QueryDataFilter filter) =>
        $"{filter.Column} = {(filter.Value is null ? "null" : filter.Value)}";

    /// <summary>Formats a count for the schema sidebar without changing query semantics.</summary>
    public static string FormatCount(long count) => count.ToString("N0", CultureInfo.InvariantCulture);
}
