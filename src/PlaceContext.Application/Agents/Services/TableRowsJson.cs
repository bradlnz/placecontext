using System.Text;
using System.Text.Json;

namespace PlaceContext.Application.Agents.Services;

/// <summary>
/// Zips table columns + rows into a JSON array of objects for embedding in an agent prompt.
/// Output is capped at <c>maxRows</c> rows and <c>maxChars</c> characters; when truncated, a
/// plain-text note line is appended after the JSON, e.g. "… (truncated: showing 50 of 213 rows)".
/// Null cells become JSON null.
/// </summary>
public static class TableRowsJson
{
    public static string Convert(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows,
        int maxRows = 50, int maxChars = 8000)
    {
        var sb = new StringBuilder();
        sb.Append("[\n");
        var shown = 0;
        var truncated = false;

        foreach (var row in rows)
        {
            if (shown >= maxRows)
            {
                truncated = true;
                break;
            }

            var obj = new Dictionary<string, string?>(columns.Count);
            for (var c = 0; c < columns.Count; c++)
                obj[columns[c]] = c < row.Count ? row[c] : null;
            var rowJson = JsonSerializer.Serialize(obj);

            // Always include at least one row, even if it alone exceeds the char budget.
            if (shown > 0 && sb.Length + rowJson.Length + 4 > maxChars)
            {
                truncated = true;
                break;
            }

            if (shown > 0)
                sb.Append(",\n");
            sb.Append("  ").Append(rowJson);
            shown++;
        }

        sb.Append("\n]");
        if (truncated)
            sb.Append($"\n… (truncated: showing {shown} of {rows.Count} rows)");
        return sb.ToString();
    }
}
