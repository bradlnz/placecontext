using System.Text.Json;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>Shared JSON object → flat string dict helpers for run forms and editors.</summary>
public static class JsonPayloadHelper
{
    /// <summary>
    /// Flatten scalar properties from one or more JSON object payloads.
    /// Nested objects/arrays are skipped. First occurrence of each key wins.
    /// </summary>
    public static Dictionary<string, string> FlattenScalars(IEnumerable<string> payloads)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var payload in payloads)
        {
            if (string.IsNullOrWhiteSpace(payload)) continue;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
                    var value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                    result.TryAdd(prop.Name, value);
                }
            }
            catch
            {
                // ignore unparseable stored payloads
            }
        }
        return result;
    }

    public static Dictionary<string, string> FlattenScalars(string? payload)
        => string.IsNullOrWhiteSpace(payload)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : FlattenScalars(new[] { payload });
}
