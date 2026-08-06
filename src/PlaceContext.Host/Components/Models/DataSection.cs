namespace PlaceContext.Host.Components.Models;

public enum DataSection
{
    Tables,
    Analytics,
    Search,
    DataMap,
    Entities
}

public sealed record DataSectionItem(DataSection Section, string Label, string RouteSegment);

public static class DataSectionNavigation
{
    public static IReadOnlyList<DataSectionItem> Items { get; } =
    [
        new(DataSection.Tables, "Tables", "data"),
        new(DataSection.Analytics, "Analytics", "analytics"),
        new(DataSection.DataMap, "Data map", "datamap"),
        new(DataSection.Entities, "Entities", "entities")
    ];

    public static string Route(Guid projectId, DataSectionItem item) =>
        $"/project/{projectId}/{item.RouteSegment}";
}
