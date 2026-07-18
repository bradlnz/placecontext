using System.Text;
using System.Text.Json;

namespace PlaceContext.Application.Features;

/// <summary>
/// Flattens JSON objects into typed leaf columns for the data map. An object with at least one
/// property recurses (dot-path keys); anything else — scalars, arrays, empty objects, nulls — is
/// a leaf. Leaf column names are the mapping's declared column plus the sanitized path
/// (<c>meta.region</c> → <c>meta_region</c>); leaf types are inferred from the value kinds seen.
/// Shared by the ingestion service (new rows) and the flattening bootstrap (existing tables), so
/// both produce the same columns from the same JSON.
/// </summary>
public static class JsonFlattener
{
    /// <summary>Postgres's identifier limit (NAMEDATALEN-1), matching the store's IdentRe.</summary>
    private const int MaxIdentifierLength = 63;

    /// <summary>One flattened leaf: its dot-path below the flattened root and the value found there.</summary>
    public readonly record struct FlatLeaf(string Path, JsonElement Value);

    /// <summary>
    /// Recursively flatten <paramref name="root"/> into leaves in document order. Objects with at
    /// least one property recurse; scalars, arrays, nulls, and EMPTY objects are leaves. A root
    /// that is not a non-empty object yields a single leaf with an empty path (itself).
    /// </summary>
    public static IReadOnlyList<FlatLeaf> Flatten(JsonElement root)
    {
        var leaves = new List<FlatLeaf>();
        Walk(root, "", leaves);
        return leaves;
    }

    private static void Walk(JsonElement el, string path, List<FlatLeaf> leaves)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            var first = true;
            foreach (var prop in el.EnumerateObject())
            {
                first = false;
                Walk(prop.Value, path.Length == 0 ? prop.Name : path + "." + prop.Name, leaves);
            }
            if (first) leaves.Add(new FlatLeaf(path, el)); // empty object {} is a leaf
            return;
        }
        leaves.Add(new FlatLeaf(path, el));
    }

    /// <summary>
    /// The column for a leaf below a flattened field: <c>{prefix}_{sanitized path}</c>. Sanitized
    /// to a Postgres-safe identifier — lowercased, every run of characters outside
    /// <c>[a-z0-9_]</c> collapses to one underscore, a leading digit gets an underscore prefix,
    /// and the result is capped at 63 chars. (The store's IdentRe validates the result again.)
    /// </summary>
    public static string ColumnName(string prefix, string leafPath)
    {
        var raw = leafPath.Length == 0 ? prefix : prefix + "_" + leafPath;
        var sb = new StringBuilder(raw.Length);
        var pendingUnderscore = false;
        foreach (var ch in raw)
        {
            var c = char.ToLowerInvariant(ch);
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                if (pendingUnderscore && sb.Length > 0) sb.Append('_');
                pendingUnderscore = false;
                sb.Append(c);
            }
            else
            {
                pendingUnderscore = true; // collapse any run of invalid chars into one '_'
            }
        }
        var name = sb.ToString().Trim('_');
        if (name.Length == 0) name = "_"; // every char was invalid — "_" is still a valid identifier
        if (char.IsDigit(name[0])) name = "_" + name;
        return name.Length > MaxIdentifierLength ? name[..MaxIdentifierLength] : name;
    }

    /// <summary>
    /// Merge one more observed value into a column's inferred type. Boolean-only → "boolean",
    /// number-only → "numeric", string-only → "text", array/empty-object-only → "jsonb";
    /// any mixture degrades to "text" (arrays land as their JSON text there). Null/Undefined
    /// values carry no kind and don't move the inference; never-seen starts null.
    /// </summary>
    public static string? MergeKind(string? current, JsonElement value)
    {
        var kind = value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => "numeric",
            JsonValueKind.String => "text",
            _ => "jsonb", // arrays and empty objects (non-empty objects never reach a leaf)
        };
        if (kind is null) return current;
        if (current is null || current == kind) return kind;
        return "text"; // mixed kinds — the one type every value's text form casts to
    }

    /// <summary>The inferred type once observation is done; defaults to "text" when only nulls were seen.</summary>
    public static string InferredType(string? current) => current ?? "text";

    /// <summary>Values travel as text and are cast server-side to the column's declared type.</summary>
    public static string? ValueText(JsonElement? el) => el is not { } v ? null : v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => v.GetRawText(), // arrays/empty objects land as their JSON text
    };
}
