using System.Globalization;

namespace PlaceContext.Host.Components.ViewModels;

public sealed record LogAnalyticsRow(IReadOnlyDictionary<string, object?> Values, QueryLogEvent? Source)
{
    public string Cell(string field) => Values.TryGetValue(field, out var value) ? Format(value) : "";
    public static string Format(object? value) => value switch
    {
        null => "",
        DateTimeOffset time => time.ToUniversalTime().ToString("O"),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
