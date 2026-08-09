using System.Text.Json;

namespace PlaceContext.App.Dashboard;

internal static class DashboardJson
{
    public static JsonElement Property(this JsonElement value, string name) => value.GetProperty(name);

    public static JsonElement PropertyOrEmptyArray(this JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array
            ? property
            : JsonSerializer.SerializeToElement(Array.Empty<object>());

    public static string String(this JsonElement value, string name)
        => value.GetProperty(name).GetString() ?? string.Empty;

    public static string? NullableString(this JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public static Guid Guid(this JsonElement value, string name) => value.GetProperty(name).GetGuid();
    public static bool Bool(this JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    public static long Int64(this JsonElement value, string name) => value.GetProperty(name).GetInt64();
    public static DateTimeOffset Date(this JsonElement value, string name) => value.GetProperty(name).GetDateTimeOffset();

    public static DateTimeOffset? NullableDate(this JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetDateTimeOffset()
            : null;
}
