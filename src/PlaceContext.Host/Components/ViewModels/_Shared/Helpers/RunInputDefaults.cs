using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

public static class RunInputDefaults
{
    public static string Generate(JobParameterDto field) => field.Type switch
    {
        "uuid" or "guid" => Guid.NewGuid().ToString(),
        "integer" or "int" or "number" when IsIdentifier(field.Name) => RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(CultureInfo.InvariantCulture),
        "checkbox" or "boolean" or "bool" => "false",
        _ => ""
    };

    private static bool IsIdentifier(string name)
    {
        var leaf = name.Split('.').Last();
        return leaf.Equals("id", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith("Id", StringComparison.Ordinal) || leaf.EndsWith("ID", StringComparison.Ordinal);
    }

    public static IReadOnlyList<JobParameterDto> Infer(IReadOnlyList<string> payloads)
    {
        // A run form edits one example; it does not combine separate shard inputs.
        foreach (var payload in payloads)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                return document.RootElement.EnumerateObject().Select(property => new JobParameterDto(property.Name,
                    Required: property.Value.ValueKind != JsonValueKind.Null, Type: property.Value.ValueKind switch
                    {
                        JsonValueKind.Number => property.Value.TryGetInt64(out _) ? "integer" : "number",
                        JsonValueKind.True or JsonValueKind.False => "checkbox",
                        JsonValueKind.String when Guid.TryParse(property.Value.GetString(), out _) => "uuid",
                        JsonValueKind.Object when property.Value.TryGetProperty("$file", out _) => "file",
                        JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null => "json",
                        _ => "text"
                    })).ToArray();
            }
            catch (JsonException) { }
        }
        return [];
    }
}
