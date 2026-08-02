namespace PlaceContext.Host;

public static class LocalityTimeZones
{
    public static IReadOnlyList<string> All { get; } = TimeZoneInfo
        .GetSystemTimeZones()
        .Select(zone => zone.Id)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
