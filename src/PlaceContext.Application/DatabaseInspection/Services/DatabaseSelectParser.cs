using System.Globalization;
using System.Text.RegularExpressions;

namespace PlaceContext.Application.DatabaseInspection;

// Compile a bounded SELECT grammar instead of sending user SQL to the application database.
public static class DatabaseSelectParser
{
    private static readonly Regex Token = new("\\G(?:\\s+|\"(?:[^\"]|\"\")*\"|'(?:[^']|'')*'|[A-Za-z_][A-Za-z_0-9]*|-?[0-9]+(?:\\.[0-9]+)?|<=|>=|<>|!=|[.,()*=<>;])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static DatabaseSelectStatement Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 16000)
        {
            throw Invalid();
        }
        var tokens = new List<string>();
        for (var position = 0; position < sql.Length;)
        {
            var match = Token.Match(sql, position);
            if (!match.Success)
            {
                throw Invalid();
            }
            position += match.Length;
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                tokens.Add(match.Value);
            }
        }
        var cursor = 0;
        bool Take(string text)
        {
            if (cursor >= tokens.Count || !tokens[cursor].Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            cursor++;
            return true;
        }
        void Require(string text)
        {
            if (!Take(text))
            {
                throw Invalid();
            }
        }
        string Identifier()
        {
            if (cursor >= tokens.Count)
            {
                throw Invalid();
            }
            var value = tokens[cursor++];
            if (value.StartsWith('"'))
            {
                return value[1..^1].Replace("\"\"", "\"");
            }
            if (!char.IsAsciiLetter(value[0]) && value[0] != '_')
            {
                throw Invalid();
            }
            return value.ToLowerInvariant();
        }
        int Number(int maximum)
        {
            if (cursor >= tokens.Count || !int.TryParse(tokens[cursor++], NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) || value > maximum)
            {
                throw Invalid();
            }
            return value;
        }
        Require("SELECT");
        var columns = new List<string>();
        var count = cursor + 1 < tokens.Count && tokens[cursor].Equals("COUNT", StringComparison.OrdinalIgnoreCase)
            && tokens[cursor + 1] == "(";
        if (count)
        {
            Require("COUNT"); Require("("); Require("*"); Require(")");
        }
        if (!count && !Take("*"))
        {
            do { columns.Add(Identifier()); } while (Take(","));
        }
        Require("FROM");
        var table = Identifier();
        table = Take(".") ? table + "." + Identifier() : "public." + table;
        var filters = new List<DatabaseFilter>();
        if (Take("WHERE"))
        {
            do
            {
                var column = Identifier();
                if (Take("IS"))
                {
                    var negate = Take("NOT");
                    Require("NULL");
                    filters.Add(new(column, negate ? "IS NOT NULL" : "IS NULL", null));
                    continue;
                }
                if (cursor >= tokens.Count)
                {
                    throw Invalid();
                }
                var operation = tokens[cursor++].ToUpperInvariant();
                if (operation is not ("=" or "!=" or "<>" or "<" or ">" or "<=" or ">=" or "LIKE" or "ILIKE") || cursor >= tokens.Count)
                {
                    throw Invalid();
                }
                var value = tokens[cursor++];
                if (value.StartsWith('\''))
                {
                    filters.Add(new(column, operation, value[1..^1].Replace("''", "'")));
                    continue;
                }
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                {
                    filters.Add(new(column, operation, value));
                    continue;
                }
                throw Invalid();
            } while (Take("AND"));
        }
        var order = new List<DatabaseSort>();
        if (Take("ORDER"))
        {
            Require("BY");
            do
            {
                var column = Identifier();
                var descending = Take("DESC");
                if (!descending)
                {
                    Take("ASC");
                }
                order.Add(new(column, descending));
            } while (Take(","));
        }
        var limit = Take("LIMIT") ? Number(500) : 100;
        var offset = Take("OFFSET") ? Number(1000000) : 0;
        Take(";");
        if (cursor != tokens.Count || limit == 0 || count && order.Count > 0)
        {
            throw Invalid();
        }
        return new(table, columns, count, filters, order, limit, offset);
    }

    private static ArgumentException Invalid() => new(
        "Use SELECT columns or COUNT(*) FROM one table, optionally WHERE comparisons joined by AND, ORDER BY, LIMIT (1–500), and OFFSET. Writes, joins, and functions are not supported for application tables.");
}
