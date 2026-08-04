using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    // ── Utilities ─────────────────────────────────────────────────────────────────────────────
    public static List<string[]> ParseCsvRecords(string text)
    {
        var records = new List<string[]>();
        var field = new System.Text.StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                        inQuotes = false;
                }
                else
                    field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        records.Add(row.ToArray());
                        row = new List<string>();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            records.Add(row.ToArray());
        }
        return records;
    }

    public static string InferType(IEnumerable<string?> values)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sample = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();
        if (sample.Count == 0)
            return DataColumnTypes.Text;
        bool All(Func<string, bool> f) => sample.All(f);
        if (All(v => v is "true" or "false" or "TRUE" or "FALSE" or "True" or "False"))
            return DataColumnTypes.Boolean;
        if (All(v => long.TryParse(v, System.Globalization.NumberStyles.Integer, ci, out _)))
            return DataColumnTypes.Bigint;
        if (All(v => decimal.TryParse(v, System.Globalization.NumberStyles.Number, ci, out _)))
            return DataColumnTypes.Numeric;
        if (All(v => Guid.TryParse(v, out _)))
            return DataColumnTypes.Uuid;
        if (All(v => DateTime.TryParse(v, ci, System.Globalization.DateTimeStyles.None, out _)))
            return DataColumnTypes.Timestamptz;
        return DataColumnTypes.Text;
    }

    public static string SanitizeIdent(string raw, int ordinal)
    {
        var lowered = (raw ?? "").Trim().ToLowerInvariant();
        var chars = lowered.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var s = System
            .Text.RegularExpressions.Regex.Replace(new string(chars), "_+", "_")
            .Trim('_');
        if (string.IsNullOrEmpty(s))
            return $"col{ordinal + 1}";
        if (!char.IsLetter(s[0]) && s[0] != '_')
            s = "c_" + s;
        return s.Length > 63 ? s[..63] : s;
    }

    public static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    public static string Trim(string message) =>
        message.Length > 400 ? message[..400] + "…" : message;
}
