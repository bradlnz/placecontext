using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Settings.Contracts.Configuration;
using PlaceContext.Settings.Persistence;

namespace PlaceContext.Settings.Configuration;

public sealed class ArtifactViewConfigService(ISettingsStore store, ICurrentTenant tenant)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ArtifactViewConfig DefaultConfig() => new(
    [
        new ArtifactCategoryRule("feasibility-reports", "Feasibility Reports", ["feasibility_v1_"]),
    ]);

    public async Task<ArtifactViewConfig> GetAsync(CancellationToken ct = default)
    {
        if (!tenant.IsResolved) return DefaultConfig();
        var json = await store.GetArtifactViewAsync(tenant.TenantId, ct);
        if (string.IsNullOrWhiteSpace(json)) return DefaultConfig();
        try
        {
            var stored = JsonSerializer.Deserialize<ArtifactViewConfig>(json, Json);
            return stored is null ? DefaultConfig() : Normalize(stored);
        }
        catch (JsonException) { return DefaultConfig(); }
    }

    public Task SaveAsync(ArtifactViewConfig config, CancellationToken ct = default)
        => tenant.IsResolved
            ? store.SetArtifactViewAsync(tenant.TenantId, JsonSerializer.Serialize(Normalize(config), Json), ct)
            : Task.CompletedTask;

    private static ArtifactViewConfig Normalize(ArtifactViewConfig config)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new List<ArtifactCategoryRule>();
        foreach (var category in config.Categories ?? [])
        {
            var label = category.Label?.Trim() ?? string.Empty;
            var prefixes = (category.Prefixes ?? [])
                .Select(prefix => prefix.Trim()).Where(prefix => prefix.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (label.Length == 0 || prefixes.Count == 0) continue;
            var baseId = Slug(!string.IsNullOrWhiteSpace(category.Id) ? category.Id : label);
            if (baseId.Length == 0) baseId = "category";
            var id = baseId;
            for (var suffix = 2; !ids.Add(id); suffix++) id = $"{baseId}-{suffix}";
            categories.Add(new ArtifactCategoryRule(id, label, prefixes));
        }
        return new ArtifactViewConfig(categories);
    }

    private static string Slug(string value) => string.Join('-', value.Trim().ToLowerInvariant()
        .Select(character => char.IsLetterOrDigit(character) ? character : '-')
        .ToArray().AsSpan().ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
}
