using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Settings.Contracts.Configuration;
using PlaceContext.Settings.Persistence;

namespace PlaceContext.Settings.Configuration;

public sealed class MenuConfigService(ISettingsStore store, ICurrentTenant tenant)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly (string Id, string? Section)[] Catalog =
    [
        ("dashboard", null), ("crm", null), ("jobs", null), ("tests", null),
        ("chains", null), ("schedules", null), ("data", null), ("vault", null),
        ("project.events", null), ("chat", null), ("artifacts", null),
        ("observability", null), ("cluster", null), ("sec-workspace", "Workspace"),
        ("overview", "Workspace"), ("wiki", "Workspace"),
        ("settings", "Workspace"), ("about", "Workspace"),
    ];

    public MenuLayout DefaultLayout() => new(Catalog.Select((item, index) =>
        new MenuItemOverride(item.Id, Order: index * 10, Section: item.Section)).ToList());

    public async Task<MenuLayout> GetLayoutAsync(CancellationToken ct = default)
    {
        if (!tenant.IsResolved) return DefaultLayout();
        var json = await store.GetMenuAsync(tenant.TenantId, ct);
        if (string.IsNullOrWhiteSpace(json)) return DefaultLayout();
        try
        {
            var stored = JsonSerializer.Deserialize<MenuLayout>(json, Json);
            return stored is null ? DefaultLayout() : Merge(stored);
        }
        catch (JsonException) { return DefaultLayout(); }
    }

    public Task SaveLayoutAsync(MenuLayout layout, CancellationToken ct = default)
        => tenant.IsResolved
            ? store.SetMenuAsync(tenant.TenantId, JsonSerializer.Serialize(layout, Json), ct)
            : Task.CompletedTask;

    private static MenuLayout Merge(MenuLayout stored)
    {
        var known = Catalog.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var result = stored.Workspace.Where(item => known.Contains(item.Id))
            .OrderBy(item => item.Order).DistinctBy(item => item.Id, StringComparer.Ordinal).ToList();
        foreach (var item in Catalog)
            if (result.All(existing => existing.Id != item.Id))
                result.Add(new MenuItemOverride(item.Id, Section: item.Section));
        return new MenuLayout(result.Select((item, index) => item with { Order = index * 10 }).ToList());
    }
}
