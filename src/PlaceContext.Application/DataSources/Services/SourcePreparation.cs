using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlaceContext.Application.DataSources;

public static class SourcePreparation
{
    public static IReadOnlyList<SourceDocument> Prepare(DataSourceDefinition source, IReadOnlyList<SourceRecord> rows, DateTimeOffset now)
    {
        var prepared = rows.Select(row => PrepareRecord(source, row, now));
        return prepared.GroupBy(document => document.Key, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            if (group.Any(document => document.Fingerprint != first.Fingerprint || document.ValidationError != first.ValidationError))
            {
                return Hold(source, first.Key, "Conflicting records share this key. Correct the source and sync again.", now);
            }
            return first;
        }).ToArray();
    }

    private static SourceDocument PrepareRecord(DataSourceDefinition source, SourceRecord row, DateTimeOffset now)
    {
        var key = string.IsNullOrWhiteSpace(row.Key) || row.Key.Length > 1024
            ? "invalid-" + Hash(row.Key + row.Json) : row.Key;
        try
        {
            if (key != row.Key || row.Json.Length > DataSourceRules.MaxRecordChars)
            {
                return Hold(source, key, "Invalid key or oversized record. Correct the source and sync again.", now);
            }
            using var json = JsonDocument.Parse(row.Json);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty(source.KeyColumn, out var value) || value.ToString() != row.Key
                || json.RootElement.EnumerateObject().Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != json.RootElement.EnumerateObject().Count())
            {
                return Hold(source, key, "Record must have unique field names and a matching key.", now);
            }
            var rules = source.Queue!;
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in json.RootElement.EnumerateObject().Where(field => source.Columns.Contains(field.Name)))
            {
                var cleaned = field.Value.Clone();
                if (rules.TrimText && field.Name != source.KeyColumn && cleaned.ValueKind == JsonValueKind.String)
                {
                    cleaned = JsonSerializer.SerializeToElement(cleaned.GetString()!.Trim());
                }
                fields[field.Name] = cleaned;
            }
            var missing = rules.RequiredFields.Any(field => !fields.TryGetValue(field, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));
            foreach (var field in rules.RedactedFields.Where(fields.ContainsKey))
            {
                fields[field] = JsonSerializer.SerializeToElement("[REDACTED]");
            }
            var cleanJson = JsonSerializer.Serialize(fields);
            if (cleanJson.Length > DataSourceRules.MaxRecordChars)
            {
                return Hold(source, key, "Prepared record exceeds the size limit.", now);
            }
            return Document(source, key, cleanJson, missing ? "A required field is missing or empty. Correct the source and sync again." : null, now);
        }
        catch (JsonException)
        {
            return Hold(source, key, "Invalid JSON. Correct the source and sync again.", now);
        }
    }

    private static SourceDocument Hold(DataSourceDefinition source, string key, string error, DateTimeOffset now)
        => Document(source, key, JsonSerializer.Serialize(new Dictionary<string, string> { [source.KeyColumn] = key }), error, now);

    private static SourceDocument Document(DataSourceDefinition source, string key, string json, string? error, DateTimeOffset now)
        => new(key, json, Hash(JsonSerializer.Serialize(new { json, error, source.Queue, source.Tags })), [], now, ValidationError: error);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
