using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>Stores the Artifacts page's ordered prefix categories as JSON on the tenant row.</summary>
public sealed class ArtifactViewConfigService : IArtifactViewConfigService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentTenant _tenant;

    public ArtifactViewConfigService(IServiceScopeFactory scopeFactory, ICurrentTenant tenant)
        => (_scopeFactory, _tenant) = (scopeFactory, tenant);

    public ArtifactViewConfig DefaultConfig() => new([]);

    public async Task<ArtifactViewConfig> GetAsync(CancellationToken ct = default)
    {
        if (!_tenant.IsResolved) return DefaultConfig();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var json = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == _tenant.TenantId)
            .Select(tenant => tenant.ArtifactViewJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(json)) return DefaultConfig();
        try
        {
            var stored = JsonSerializer.Deserialize<ArtifactViewConfig>(json, Json);
            return stored is null ? DefaultConfig() : Normalize(stored);
        }
        catch
        {
            return DefaultConfig();
        }
    }

    public async Task SaveAsync(ArtifactViewConfig config, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved) return;
        var normalized = Normalize(config);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == _tenant.TenantId, ct);
        if (row is null) return;
        row.ArtifactViewJson = JsonSerializer.Serialize(normalized, Json);
        await db.SaveChangesAsync(ct);
    }

    private static ArtifactViewConfig Normalize(ArtifactViewConfig config)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new List<ArtifactCategoryRule>();
        foreach (var category in config.Categories ?? Array.Empty<ArtifactCategoryRule>())
        {
            var label = category.Label?.Trim() ?? "";
            var prefixes = (category.Prefixes ?? Array.Empty<string>())
                .Select(prefix => prefix.Trim())
                .Where(prefix => prefix.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (label.Length == 0 || prefixes.Count == 0) continue;

            var baseId = Slug(!string.IsNullOrWhiteSpace(category.Id) ? category.Id : label);
            if (baseId.Length == 0) baseId = "category";
            var id = baseId;
            for (var suffix = 2; !ids.Add(id); suffix++) id = $"{baseId}-{suffix}";
            categories.Add(new ArtifactCategoryRule(id, label, prefixes));
        }
        return new ArtifactViewConfig(categories);
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return string.Join('-', new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
