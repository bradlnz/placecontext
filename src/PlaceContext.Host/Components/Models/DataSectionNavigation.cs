namespace PlaceContext.Host.Components.Models;

public static class DataSectionNavigation
{
    public static IReadOnlyList<DataSectionItem> Items { get; } =
    [
        new(DataSection.Tables, "Records", "data"),
        new(DataSection.Analytics, "Analytics", "analytics"),
        new(DataSection.DataMap, "Data map", "datamap"),
        new(DataSection.Entities, "Entities", "entities"),
        new(DataSection.Graph, "Graph", "data-graph")
    ];

    public static string Route(Guid projectId, DataSectionItem item) =>
        $"/project/{projectId}/{item.RouteSegment}";
}
