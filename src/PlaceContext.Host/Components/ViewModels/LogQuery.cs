using System.Globalization;
using System.Text;

namespace PlaceContext.Host.Components.ViewModels;

// A deliberately small query language: values are compared in memory, never interpolated into SQL.
public sealed class LogQuery
{
    public const string Fields = "job, project, run, status, level, stream, message, timestamp, source, service";
    private readonly List<(string Value, bool Quoted)> _tokens = [];
    private readonly DateTimeOffset _now;
    private int _position;
    private int _depth;
    private readonly HashSet<string>? _availableFields;
    private Func<Func<string, string>, bool> _matches = _ => true;
    public bool Matches(QueryLogEvent row) => _matches(row.Field);
    public bool MatchesValues(IReadOnlyDictionary<string, object?> values) => _matches(field =>
        values.TryGetValue(field, out var value) ? LogAnalyticsRow.Format(value) : "");
    public string OrderField { get; private set; } = "timestamp";
    public bool Descending { get; private set; } = true;

    public LogQuery(string text, DateTimeOffset now, IEnumerable<string>? availableFields = null)
    {
        _now = now;
        _availableFields = availableFields is null ? null : new(availableFields, StringComparer.OrdinalIgnoreCase);
        if (text.Length > 4096)
        {
            throw new ArgumentException("Queries must be at most 4,096 characters.");
        }
        Tokenize(text);
        if (_tokens.Count > 256)
        {
            throw new ArgumentException("Query is too complex; use fewer conditions.");
        }
        if (_tokens.Count > 0 && !Is("ORDER"))
        {
            _matches = Or();
        }
        if (Take("ORDER"))
        {
            Expect("BY");
            OrderField = FieldName();
            Descending = Take("DESC");
            if (!Descending)
            {
                Take("ASC");
            }
        }
        if (_position != _tokens.Count)
        {
            throw Error("Unexpected token");
        }
    }

    public IEnumerable<QueryLogEvent> Sort(IEnumerable<QueryLogEvent> rows)
    {
        if (OrderField == "timestamp")
        {
            var knownFirst = rows.OrderBy(row => row.Timestamp is null);
            return Descending ? knownFirst.ThenByDescending(row => row.Timestamp) : knownFirst.ThenBy(row => row.Timestamp);
        }
        return Descending ? rows.OrderByDescending(row => row.Field(OrderField), StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(row => row.Field(OrderField), StringComparer.OrdinalIgnoreCase);
    }

    private Func<Func<string, string>, bool> Or()
    {
        var left = And();
        while (Take("OR"))
        {
            var previous = left;
            var right = And();
            left = row => previous(row) || right(row);
        }
        return left;
    }

    private Func<Func<string, string>, bool> And()
    {
        var left = Condition();
        while (Take("AND"))
        {
            var previous = left;
            var right = Condition();
            left = row => previous(row) && right(row);
        }
        return left;
    }

    private Func<Func<string, string>, bool> Condition()
    {
        if (++_depth > 16)
        {
            throw Error("Use at most 16 nested groups");
        }
        try
        {
            if (Take("NOT"))
            {
                var inner = Condition();
                return row => !inner(row);
            }
            if (Take("("))
            {
                var inner = Or();
                Expect(")");
                return inner;
            }
            var field = FieldName();
            var negate = Take("NOT");
            if (Take("IN"))
            {
                if (field == "timestamp")
                {
                    throw Error("Use a comparison for timestamp");
                }
                Expect("(");
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Value() };
                while (Take(","))
                {
                    values.Add(Value());
                }
                Expect(")");
                return row => negate != values.Contains(row(field));
            }
            var op = (Take("!") ? "!" + Value() : Value()).ToLowerInvariant();
            if (op is "contains" or "!contains" or "contains_cs" or "!contains_cs")
            {
                if (field == "timestamp")
                {
                    throw Error("Timestamp does not support text matching");
                }
                var text = Value();
                var comparison = op.EndsWith("_cs", StringComparison.Ordinal)
                    ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var excluded = negate ^ op.StartsWith('!');
                return row => excluded != row(field).Contains(text, comparison);
            }
            if (negate)
            {
                throw Error("Expected IN or contains after NOT");
            }
            op = op == "==" ? "=" : op;
            if (op is not ("=" or "!=" or "~" or "!~" or ">" or ">=" or "<" or "<="))
            {
                throw Error("Expected contains, !contains, contains_cs, !contains_cs, =, !=, ~, !~, IN, or a range comparison");
            }
            var value = Value();
            if (field == "timestamp")
            {
                if (op is "~" or "!~")
                {
                    throw Error("Timestamp does not support text matching");
                }
                var target = Time(value);
                return row => DateTimeOffset.TryParse(row(field), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actual) && Compare(actual.CompareTo(target), op);
            }
            if (op is ">" or ">=" or "<" or "<=")
            {
                if (_availableFields is null)
                {
                    throw Error("Only timestamp supports range comparisons");
                }
                if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
                {
                    throw Error("Use a number for this comparison");
                }
                return row => decimal.TryParse(row(field), NumberStyles.Float, CultureInfo.InvariantCulture, out var actual)
                                    && Compare(actual.CompareTo(target), op);
            }
            return row => op switch
            {
                "~" => row(field).Contains(value, StringComparison.OrdinalIgnoreCase),
                "!~" => !row(field).Contains(value, StringComparison.OrdinalIgnoreCase),
                "=" => string.Equals(row(field), value, StringComparison.OrdinalIgnoreCase),
                _ => !string.Equals(row(field), value, StringComparison.OrdinalIgnoreCase),
            };
        }
        finally
        {
            _depth--;
        }
    }

    private DateTimeOffset Time(string value)
    {
        if (value.Length > 2 && value[0] == '-' && int.TryParse(value[1..^1], out var amount)
            && amount is >= 0 and <= 365000)
        {
            var seconds = char.ToLowerInvariant(value[^1]) switch
            {
                's' => 1,
                'm' => 60,
                'h' => 3600,
                'd' => 86400,
                _ => 0
            };
            if (seconds > 0)
            {
                return _now.AddSeconds(-(double)amount * seconds);
            }
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            return time;
        }
        throw Error("Use an ISO timestamp or a relative time such as -15m, -1h, or -7d");
    }

    private static bool Compare(int comparison, string op) => op switch
    {
        "=" => comparison == 0,
        "!=" => comparison != 0,
        ">" => comparison > 0,
        ">=" => comparison >= 0,
        "<" => comparison < 0,
        _ => comparison <= 0,
    };

    private string FieldName()
    {
        var field = Value().ToLowerInvariant();
        if (_availableFields is not null)
        {
            if (!_availableFields.Contains(field))
            {
                throw Error($"Unknown field '{field}'");
            }
            return field;
        }
        if (field is not ("job" or "project" or "run" or "status" or "level" or "stream" or "message" or "timestamp"))
        {
            throw Error($"Unknown field '{field}'. Use {Fields}");
        }
        return field;
    }

    private string Value()
    {
        if (_position >= _tokens.Count)
        {
            throw Error("Expected a value");
        }
        var token = _tokens[_position++];
        if (!token.Quoted && token.Value is "(" or ")" or ",")
        {
            throw Error("Expected a value; quote text containing punctuation");
        }
        return token.Value;
    }
    private bool Is(string value) => _position < _tokens.Count && !_tokens[_position].Quoted
        && string.Equals(_tokens[_position].Value, value, StringComparison.OrdinalIgnoreCase);
    private bool Take(string value)
    {
        if (!Is(value))
        {
            return false;
        }
        _position++;
        return true;
    }
    private void Expect(string value)
    {
        if (!Take(value))
        {
            throw Error($"Expected {value}");
        }
    }
    private ArgumentException Error(string message) => new($"{message} (near token {_position + 1}).");

    private void Tokenize(string text)
    {
        for (var i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }
            if (text[i] is '\'' or '"')
            {
                var quote = text[i++];
                var value = new StringBuilder();
                var closed = false;
                while (i < text.Length)
                {
                    var c = text[i++];
                    if (c == quote)
                    {
                        closed = true;
                        break;
                    }
                    if (c == '\\' && i < text.Length)
                    {
                        c = text[i++];
                    }
                    value.Append(c);
                }
                if (!closed)
                {
                    throw Error("Unclosed quoted value");
                }
                _tokens.Add((value.ToString(), true));
                continue;
            }
            if ("() ,=~!<>".Contains(text[i]))
            {
                var op = text[i++].ToString();
                if (i < text.Length && (op is "!" or "<" or ">" or "=") && (text[i] == '=' || op == "!" && text[i] == '~'))
                {
                    op += text[i++];
                }
                _tokens.Add((op, false));
                continue;
            }
            {
                var start = i++;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && !"(),=~!<>'\"".Contains(text[i]))
                {
                    i++;
                }
                _tokens.Add((text[start..i], false));
            }
        }
    }
}
