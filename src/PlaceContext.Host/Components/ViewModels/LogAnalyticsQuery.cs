using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlaceContext.Host.Components.ViewModels;

// Executes a bounded pipeline over authorized log events; no query text reaches a database or shell.
public sealed class LogAnalyticsQuery
{
    public const string DefaultQuery = "fields timestamp, level, job, message\n| sort timestamp desc\n| limit 100";
    private readonly string[] _stages;
    private readonly DateTimeOffset _now;
    private static readonly string[] DefaultFields = ["timestamp", "level", "job", "project", "stream", "message", "run", "status"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public LogAnalyticsQuery(string text, DateTimeOffset now)
    {
        if (text.Length > 4096)
        {
            throw new ArgumentException("Queries must be at most 4,096 characters.");
        }
        _stages = Split(text, '|');
        if (_stages.Length > 32)
        {
            throw new ArgumentException("Use at most 32 pipeline commands.");
        }
        _now = now;
    }

    public LogAnalyticsResult Execute(IEnumerable<QueryLogEvent> events, IReadOnlyDictionary<string, string>? mappings = null)
    {
        if (mappings is not null)
        {
            foreach (var (target, source) in mappings)
            {
                if (target is not ("timestamp" or "level" or "message" or "stream"))
                {
                    throw new ArgumentException($"Cannot map log field '{target}'.");
                }
                if (!string.IsNullOrWhiteSpace(source) && (source.Length > 256 || !source.Trim().StartsWith("json.", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("Use a JSON field path such as json.event.severity.");
                }
            }
        }
        var rows = events.Select(entry => ToRow(entry, mappings)).ToList();
        var columns = DefaultFields.ToList();
        var available = new HashSet<string>(DefaultFields, StringComparer.OrdinalIgnoreCase);
        available.UnionWith(["source", "service"]);
        foreach (var row in rows)
        {
            available.UnionWith(row.Values.Keys);
        }
        var sourceFields = available.Where(field => field.StartsWith("json.", StringComparison.OrdinalIgnoreCase)).Order().ToArray();
        var matched = rows.Count;
        var aggregated = false;
        for (var index = 0; index < _stages.Length; index++)
        {
            var stage = _stages[index].Trim();
            if (stage.Length == 0)
            {
                if (_stages.Length == 1)
                {
                    continue;
                }
                throw new ArgumentException($"Empty pipeline command at position {index + 1}.");
            }
            var split = stage.IndexOfAny([' ', '\t', '\r', '\n']);
            var command = (split < 0 ? stage : stage[..split]).ToLowerInvariant();
            var argument = split < 0 ? "" : stage[(split + 1)..].Trim();
            switch (command)
            {
                case "logs" when index == 0 && argument.Length == 0:
                    break;
                case "filter":
                case "where":
                    if (argument.Length == 0)
                    {
                        throw new ArgumentException("Filter requires a condition.");
                    }
                    var filter = new LogQuery(argument, _now, available);
                    rows = rows.Where(row => filter.MatchesValues(row.Values)).ToList();
                    if (!aggregated)
                    {
                        matched = rows.Count;
                    }
                    break;
                case "fields":
                case "project":
                    var selections = Split(argument, ',').Select(item => Selection(item, available)).ToArray();
                    CheckColumns(selections.Select(item => item.Alias));
                    rows = rows.Select(row => new LogAnalyticsRow(selections.ToDictionary(item => item.Alias,
                        item => row.Values.GetValueOrDefault(item.Field), StringComparer.OrdinalIgnoreCase), row.Source)).ToList();
                    columns = selections.Select(item => item.Alias).ToList();
                    available = new(columns, StringComparer.OrdinalIgnoreCase);
                    break;
                case "stats":
                case "summarize":
                    var by = Regex.Match(argument, @"\s+by\s+", RegexOptions.IgnoreCase, RegexTimeout);
                    var aggregateText = by.Success ? argument[..by.Index] : argument;
                    var groupText = by.Success ? argument[(by.Index + by.Length)..] : null;
                    var metrics = Split(aggregateText, ',').Select(item => Metric(item, available)).ToArray();
                    var groups = groupText is null ? [] : Split(groupText, ',').Select(item => Group(item, available)).ToArray();
                    columns = groups.Select(item => item.Alias).Concat(metrics.Select(item => item.Alias)).ToList();
                    CheckColumns(columns);
                    var buckets = rows.GroupBy(row => JsonSerializer.Serialize(groups.Select(group => group.Value(row)).ToArray()));
                    var result = new List<LogAnalyticsRow>();
                    foreach (var bucket in buckets)
                    {
                        var cells = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var group in groups)
                        {
                            cells[group.Alias] = group.Value(bucket.First());
                        }
                        foreach (var metric in metrics)
                        {
                            cells[metric.Alias] = metric.Value(bucket);
                        }
                        result.Add(new(cells, null));
                    }
                    if (rows.Count == 0 && groups.Length == 0)
                    {
                        result.Add(new(metrics.ToDictionary(metric => metric.Alias, metric => metric.Value([]), StringComparer.OrdinalIgnoreCase), null));
                    }
                    rows = result;
                    available = new(columns, StringComparer.OrdinalIgnoreCase);
                    aggregated = true;
                    break;
                case "sort":
                    argument = Regex.Replace(argument, @"^by\s+", "", RegexOptions.IgnoreCase, RegexTimeout);
                    IOrderedEnumerable<LogAnalyticsRow>? ordered = null;
                    foreach (var item in Split(argument, ','))
                    {
                        var terms = item.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (terms.Length is < 1 or > 2 || terms.Length == 2 && terms[1].ToLowerInvariant() is not ("asc" or "desc"))
                        {
                            throw new ArgumentException("Use sort field asc or desc, separated by commas.");
                        }
                        var field = Field(terms[0], available);
                        var descending = terms.Length == 2 && terms[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
                        var comparer = Comparer<object?>.Create((left, right) =>
                        {
                            if (left is null || right is null)
                            {
                                return left is null ? right is null ? 0 : 1 : -1;
                            }
                            var comparison = left is DateTimeOffset a && right is DateTimeOffset b ? a.CompareTo(b)
                                                            : Number(left, out var x) && Number(right, out var y) ? x.CompareTo(y)
                                                            : StringComparer.OrdinalIgnoreCase.Compare(LogAnalyticsRow.Format(left), LogAnalyticsRow.Format(right));
                            return descending ? -comparison : comparison;
                        });
                        ordered = ordered is null ? rows.OrderBy(row => row.Values.GetValueOrDefault(field), comparer)
                            : ordered.ThenBy(row => row.Values.GetValueOrDefault(field), comparer);
                    }
                    rows = ordered!.ToList();
                    break;
                case "limit":
                case "take":
                    if (!int.TryParse(argument, out var count) || count is < 1 or > 500)
                    {
                        throw new ArgumentException("Limit must be between 1 and 500.");
                    }
                    rows = rows.Take(count).ToList();
                    break;
                default:
                    // Preserve existing saved filter expressions while making pipelines the default editor experience.
                    if (_stages.Length != 1)
                    {
                        throw new ArgumentException($"Unknown command '{command}'. Use fields, filter, stats, sort, or limit.");
                    }
                    var legacy = new LogQuery(stage, _now);
                    var filtered = rows.Where(row => legacy.MatchesValues(row.Values));
                    rows = (legacy.Descending
                        ? filtered.OrderByDescending(row => row.Cell(legacy.OrderField), StringComparer.OrdinalIgnoreCase)
                        : filtered.OrderBy(row => row.Cell(legacy.OrderField), StringComparer.OrdinalIgnoreCase)).ToList();
                    matched = rows.Count;
                    break;
            }
        }
        return new(columns, rows.Take(500).ToList(), rows.Count, matched, aggregated)
        {
            SourceFields = sourceFields
        };
    }

    internal static LogAnalyticsRow ToRow(QueryLogEvent entry, IReadOnlyDictionary<string, string>? mappings)
    {
        var cells = DefaultFields.ToDictionary(field => field, field => field == "timestamp"
            ? (object?)entry.Timestamp : entry.Field(field), StringComparer.OrdinalIgnoreCase);
        cells["source"] = entry.Field("source");
        cells["service"] = entry.Field("service");
        if (entry.Event.Message.TrimStart().StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(entry.Event.Message, new JsonDocumentOptions { MaxDepth = 16 });
                void Flatten(JsonElement value, string path, int depth)
                {
                    if (cells.Count >= 72 || depth > 8)
                    {
                        return;
                    }
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in value.EnumerateObject())
                        {
                            Flatten(property.Value, path + "." + property.Name, depth + 1);
                        }
                        return;
                    }
                    cells[path] = value.ValueKind switch
                    {
                        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
                        JsonValueKind.Null => null,
                        JsonValueKind.String => value.GetString(),
                        _ => value.GetRawText(),
                    };
                }
                Flatten(json.RootElement, "json", 0);
            }
            catch (JsonException) { }
        }
        if (mappings is not null)
        {
            foreach (var (target, source) in mappings)
            {
                if (string.IsNullOrWhiteSpace(source) || !cells.TryGetValue(source.Trim(), out var value) || value is null)
                {
                    continue;
                }
                var text = LogAnalyticsRow.Format(value);
                if (target == "timestamp")
                {
                    if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
                    {
                        cells[target] = time;
                    }
                    continue;
                }
                cells[target] = target == "level" ? text.ToUpperInvariant() switch
                {
                    "INFORMATION" => "INFO",
                    "WARNING" => "WARN",
                    "CRITICAL" => "FATAL",
                    _ => text.ToUpperInvariant(),
                } : value;
            }
        }
        return new(cells, entry);
    }

    private static (string Field, string Alias) Selection(string text, HashSet<string> available)
    {
        var parts = Regex.Split(text.Trim(), @"\s+as\s+", RegexOptions.IgnoreCase, RegexTimeout);
        if (parts.Length > 2)
        {
            throw new ArgumentException("Use field as alias.");
        }
        var field = Field(parts[0], available);
        return (field, parts.Length == 2 ? Identifier(parts[1]) : field);
    }

    private static (string Alias, Func<IEnumerable<LogAnalyticsRow>, object?> Value) Metric(string text, HashSet<string> available)
    {
        var match = Regex.Match(text.Trim(), @"^(count|count_distinct|sum|avg|min|max)\(\s*([@\w.*]*)\s*\)(?:\s+as\s+([\w.]+))?$", RegexOptions.IgnoreCase, RegexTimeout);
        if (!match.Success)
        {
            throw new ArgumentException("Use count(), count_distinct(field), sum(field), avg(field), min(field), or max(field), optionally as alias.");
        }
        var function = match.Groups[1].Value.ToLowerInvariant();
        var argument = match.Groups[2].Value;
        var field = function == "count" && argument is "" or "*" ? null : Field(argument, available);
        var alias = match.Groups[3].Success ? Identifier(match.Groups[3].Value) : function + (field is null ? "" : "_" + field.Replace('.', '_'));
        object? Aggregate(IEnumerable<LogAnalyticsRow> source)
        {
            var rows = source.ToList();
            if (function == "count")
            {
                return field is null ? rows.Count : rows.Count(row => row.Values.GetValueOrDefault(field) is not null);
            }
            var values = rows.Select(row => row.Values.GetValueOrDefault(field!)).Where(value => value is not null).ToArray();
            if (function == "count_distinct")
            {
                return values.Select(LogAnalyticsRow.Format).Distinct(StringComparer.Ordinal).Count();
            }
            var numbers = values.Where(value => Number(value, out _)).Select(value =>
            {
                Number(value, out var number);
                return number;
            }).ToArray();
            if (numbers.Length == 0)
            {
                return function == "sum" ? 0m : null;
            }
            return function switch
            {
                "sum" => numbers.Sum(),
                "avg" => numbers.Average(),
                "min" => numbers.Min(),
                _ => numbers.Max()
            };
        }
        return (alias, Aggregate);
    }

    private static (string Alias, Func<LogAnalyticsRow, object?> Value) Group(string text, HashSet<string> available)
    {
        var match = Regex.Match(text.Trim(), @"^bin\(\s*(?:(@?timestamp)\s*,\s*)?(\d+)([smhd])\s*\)(?:\s+as\s+([\w.]+))?$", RegexOptions.IgnoreCase, RegexTimeout);
        if (match.Success)
        {
            Field("timestamp", available);
            if (!int.TryParse(match.Groups[2].Value, out var amount) || amount is < 1 or > 365000)
            {
                throw new ArgumentException("Invalid bin size.");
            }
            var seconds = amount * (match.Groups[3].Value.ToLowerInvariant() switch
            {
                "s" => 1L,
                "m" => 60L,
                "h" => 3600L,
                _ => 86400L
            });
            var alias = match.Groups[4].Success ? Identifier(match.Groups[4].Value) : "timestamp";
            return (alias, row => row.Values.GetValueOrDefault("timestamp") is DateTimeOffset time
                ? DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(time.ToUnixTimeSeconds() / (double)seconds) * seconds) : null);
        }
        var selection = Selection(text, available);
        return (selection.Alias, row => row.Values.GetValueOrDefault(selection.Field));
    }

    private static bool Number(object? value, out decimal number) => decimal.TryParse(LogAnalyticsRow.Format(value),
        NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    private static string Field(string text, HashSet<string> available)
    {
        var field = Identifier(text.Trim().TrimStart('@'));
        if (!available.Contains(field))
        {
            throw new ArgumentException($"Unknown field '{field}'. JSON fields use json.name and must exist in the searched events.");
        }
        return field;
    }
    private static string Identifier(string text)
    {
        if (!Regex.IsMatch(text, @"^[A-Za-z_][\w.]*$", RegexOptions.CultureInvariant, RegexTimeout))
        {
            throw new ArgumentException($"Invalid field or alias '{text}'.");
        }
        return text;
    }
    private static void CheckColumns(IEnumerable<string> columns)
    {
        var names = columns.ToArray();
        if (names.Length is < 1 or > 16 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
        {
            throw new ArgumentException("Select 1–16 distinct columns; give duplicate expressions different aliases.");
        }
    }
    private static string[] Split(string text, char separator)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (quote != '\0')
            {
                if (character == '\\')
                {
                    i++;
                    continue;
                }
                if (character == quote)
                {
                    quote = '\0';
                }
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (character == '(')
            {
                if (++depth > 16)
                {
                    throw new ArgumentException("Use at most 16 nested groups.");
                }
                continue;
            }
            if (character == ')')
            {
                if (--depth < 0)
                {
                    throw new ArgumentException("Unexpected closing parenthesis.");
                }
                continue;
            }
            if (character == separator && depth == 0)
            {
                result.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        if (quote != '\0' || depth != 0)
        {
            throw new ArgumentException("Unclosed quote or parenthesis.");
        }
        result.Add(text[start..].Trim());
        return result.ToArray();
    }
}
