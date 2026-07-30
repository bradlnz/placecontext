using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaceContext.Domain.ValueObjects;

[JsonDerivedType(typeof(NoGate), "none")]
[JsonDerivedType(typeof(WaitGate), "wait")]
[JsonDerivedType(typeof(ConditionGate), "condition")]
public abstract record ChainGate
{
    /// <summary>Evaluates the gate against the current pipeline payload.</summary>
    public abstract GateResult Evaluate(string? payload);
}

/// <summary>No gate — the stage runs unconditionally (the default, backward compatible).</summary>
public sealed record NoGate : ChainGate
{
    public static readonly NoGate Instance = new();

    public override GateResult Evaluate(string? payload)
        => new(true, null);
}

/// <summary>Pauses the pipeline for a given duration before the stage executes.</summary>
public sealed record WaitGate(TimeSpan Duration) : ChainGate
{
    public override GateResult Evaluate(string? payload)
        => new(true, Duration);
}

/// <summary>
/// Routes the pipeline based on a JSONPath-style expression evaluated against the current payload.
/// When the expression evaluates to true the stage runs normally; when false the stage is skipped
/// (and <see cref="ElseBranch"/> stages run instead, if present).
/// </summary>
public sealed record ConditionGate(string Expression, IReadOnlyList<ChainStage>? ElseBranch = null) : ChainGate
{
    public override GateResult Evaluate(string? payload)
    {
        var result = EvaluateExpression(Expression, payload);
        return new GateResult(result, null);
    }

    /// <summary>
    /// Simple expression evaluator supporting:
    ///   - <c>exists:field.path</c> — true when the JSON path exists and is not null
    ///   - <c>eq:field.path:value</c> / <c>neq:field.path:value</c> — equality
    ///   - <c>contains:field.path:value</c> — string/array contains value
    ///   - <c>starts:field.path:value</c> / <c>ends:field.path:value</c> — string matching
    ///   - <c>in:field.path:a,b,c</c> — value belongs to a comma-separated set
    ///   - <c>empty:field.path</c> / <c>notempty:field.path</c> — empty-value checks
    ///   - <c>gt:field.path:number</c> — true when the numeric value at the path is greater than
    ///   - <c>gte:field.path:number</c> — greater than or equal
    ///   - <c>lt:field.path:number</c> — less than
    ///   - <c>lte:field.path:number</c> — less than or equal
    ///   - <c>true</c> / <c>false</c> — literal
    /// </summary>
    internal static bool EvaluateExpression(string expression, string? payload)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var trimmed = expression.Trim();

        // Literal booleans
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return false;

        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var colon = trimmed.IndexOf(':');
            if (colon < 0) return false;

            var op = trimmed[..colon].Trim().ToLowerInvariant();
            var rest = trimmed[(colon + 1)..].Trim();

            return op switch
            {
                "exists" => JsonPathExists(root, rest),
                "notexists" => !JsonPathExists(root, rest),
                "eq" => EvaluateEq(root, rest),
                "neq" => !EvaluateEq(root, rest),
                "contains" => EvaluateText(root, rest, TextComparison.Contains),
                "starts" => EvaluateText(root, rest, TextComparison.StartsWith),
                "ends" => EvaluateText(root, rest, TextComparison.EndsWith),
                "in" => EvaluateIn(root, rest),
                "empty" => EvaluateEmpty(root, rest),
                "notempty" => !EvaluateEmpty(root, rest),
                "gt" => EvaluateCompare(root, rest, (v, t) => v > t),
                "gte" => EvaluateCompare(root, rest, (v, t) => v >= t),
                "lt" => EvaluateCompare(root, rest, (v, t) => v < t),
                "lte" => EvaluateCompare(root, rest, (v, t) => v <= t),
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool JsonPathExists(JsonElement root, string path)
    {
        var el = ResolvePath(root, path);
        return el.HasValue && el.Value.ValueKind != JsonValueKind.Null;
    }

    private static bool EvaluateEq(JsonElement root, string rest)
    {
        // rest is "field.path:value" or "field.path" when comparing to true
        var lastColon = rest.LastIndexOf(':');
        if (lastColon <= 0) return false;

        var path = rest[..lastColon].Trim();
        var expected = rest[(lastColon + 1)..].Trim();

        var el = ResolvePath(root, path);
        if (el is null) return false;

        return el.Value.ValueKind switch
        {
            JsonValueKind.String => el.Value.GetString() == expected,
            JsonValueKind.Number => el.Value.GetRawText() == expected,
            JsonValueKind.True => expected is "true" or "1",
            JsonValueKind.False => expected is "false" or "0",
            _ => el.Value.GetRawText() == expected,
        };
    }

    private static bool EvaluateCompare(JsonElement root, string rest, Func<double, double, bool> cmp)
    {
        var lastColon = rest.LastIndexOf(':');
        if (lastColon <= 0) return false;

        var path = rest[..lastColon].Trim();
        if (!double.TryParse(rest[(lastColon + 1)..].Trim(), out var target))
            return false;

        var el = ResolvePath(root, path);
        if (el is null || el.Value.ValueKind != JsonValueKind.Number)
            return false;

        return cmp(el.Value.GetDouble(), target);
    }

    private enum TextComparison { Contains, StartsWith, EndsWith }

    private static bool EvaluateText(JsonElement root, string rest, TextComparison comparison)
    {
        if (!TrySplitOperand(rest, out var path, out var expected)) return false;
        var el = ResolvePath(root, path);
        if (el is null) return false;

        if (comparison == TextComparison.Contains && el.Value.ValueKind == JsonValueKind.Array)
            return el.Value.EnumerateArray().Any(item =>
                string.Equals(ScalarText(item), expected, StringComparison.OrdinalIgnoreCase));

        var actual = ScalarText(el.Value);
        if (actual is null) return false;
        return comparison switch
        {
            TextComparison.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            TextComparison.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            TextComparison.EndsWith => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool EvaluateIn(JsonElement root, string rest)
    {
        if (!TrySplitOperand(rest, out var path, out var values)) return false;
        var el = ResolvePath(root, path);
        var actual = el is null ? null : ScalarText(el.Value);
        if (actual is null) return false;
        return values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => string.Equals(candidate, actual, StringComparison.OrdinalIgnoreCase));
    }

    private static bool EvaluateEmpty(JsonElement root, string path)
    {
        var el = ResolvePath(root, path);
        if (el is null || el.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        return el.Value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(el.Value.GetString()),
            JsonValueKind.Array => el.Value.GetArrayLength() == 0,
            JsonValueKind.Object => !el.Value.EnumerateObject().Any(),
            _ => false,
        };
    }

    private static bool TrySplitOperand(string rest, out string path, out string value)
    {
        var separator = rest.LastIndexOf(':');
        if (separator <= 0)
        {
            path = "";
            value = "";
            return false;
        }
        path = rest[..separator].Trim();
        value = rest[(separator + 1)..].Trim();
        return path.Length > 0;
    }

    private static string? ScalarText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
        _ => null,
    };

    private static JsonElement? ResolvePath(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return root;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonElement current = root;
        for (var i = 0; i < parts.Length; i++)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(parts[i], out current)) return null;
        }
        return current;
    }
}

/// <summary>Result of evaluating a gate against the current pipeline payload.</summary>
public sealed record GateResult(bool Proceed, TimeSpan? WaitDuration);
