using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>
/// Tenant menu customization stored as JSON on the tenant row. Built-in catalog provides ids,
/// default labels, hrefs, and required permissions; overrides control order/label/visibility/section.
/// </summary>
public sealed class MenuConfigService : IMenuConfigService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentTenant _tenant;
    private readonly IPermissionService _perms;

    public MenuConfigService(IServiceScopeFactory scopeFactory, ICurrentTenant tenant, IPermissionService perms)
    {
        _scopeFactory = scopeFactory;
        _tenant = tenant;
        _perms = perms;
    }

    /// <summary>Built-in catalog — ids are stable; do not rename without a migration of stored layouts.</summary>
    internal static readonly CatalogItem[] WorkspaceCatalog =
    {
        new("dashboard", "Dashboard", "link", "/", Permission.ProjectsView, "grid", null),
        new("project.overview", "Project overview", "link", "/project/{projectId}", Permission.ProjectsView, "grid", null),
        new("crm", "CRM", "link", "/project/{projectId}/crm", Permission.DataRead, "users", null),
        new("jobs", "Jobs", "link", "/project/{projectId}/jobs", Permission.JobsView, "box", null),
        new("chains", "Chains", "link", "/project/{projectId}/chains", Permission.JobsView, "chain", null),
        new("schedules", "Schedules", "link", "/project/{projectId}/schedules", Permission.JobsView, "clock", null),
        new("data", "Data", "entities", "/project/{projectId}/data", Permission.DataRead, "map", null),
        new("data.tables", "Tables", "link", "/project/{projectId}/data", Permission.DataRead, null, null, "data"),
        new("data.analytics", "Analytics", "link", "/project/{projectId}/analytics", Permission.DataRead, null, null, "data"),
        new("data.datamap", "Data map", "link", "/project/{projectId}/datamap", Permission.DataRead, null, null, "data"),
        new("data.entities", "Entities", "link", "/project/{projectId}/entities", Permission.DataRead, null, null, "data"),
        new("vault", "Vault", "link", "/project/{projectId}/secrets", Permission.SecretsManage, "key", null),
        new("project.events", "Events", "link", "/project/{projectId}/events", Permission.EventsManage, "pulse", null),
        new("chat", "Chat", "link", "/chat", Permission.AgentsChat, "chat", null),
        new("artifacts", "Artifacts", "link", "/artifacts", Permission.ArtifactsView, "file", null),
        new("observability", "Observability", "link", "/observability", Permission.JobsView, "pulse", null),
        new("cluster", "Cluster", "link", "/cluster", Permission.SettingsManage, "box", null),
        new("sec-workspace", "Workspace", "section", null, null, null, "Workspace"),
        new("overview", "Projects overview", "link", "/overview", Permission.ProjectsView, "pulse", "Workspace"),
        new("onboarding", "Onboarding", "link", "/onboarding", Permission.ProjectsView, "rocket", "Workspace"),
        new("wiki", "Wiki", "link", "/wiki", null, "ledger", "Workspace"),
        new("settings", "Settings", "link", "/settings/branding", Permission.SettingsManage, "key", "Workspace"),
        new("about", "About", "link", "/about", null, "grid", "Workspace"),
    };

    internal sealed record CatalogItem(
        string Id, string DefaultLabel, string Kind, string? HrefTemplate,
        string? RequiredPermission, string? Icon, string? DefaultSection, string? Parent = null);

    public MenuLayout DefaultLayout()
    {
        var ws = WorkspaceCatalog.Select((c, i) => new MenuItemOverride(
            c.Id, null, i * 10, true, c.DefaultSection)).ToList();
        return new MenuLayout(ws);
    }

    public async Task<MenuLayout> GetLayoutAsync(CancellationToken ct = default)
    {
        if (!_tenant.IsResolved) return DefaultLayout();
        // Own scope (short-lived AppDbContext): this runs from MainLayout/ProjectLayout render,
        // concurrently with the page's own load on the circuit-shared scoped context. Reading the
        // tenant row on that shared context races the page ("A second operation was started on this
        // context instance"). Mirror PermissionService — the ambient tenant (AsyncLocal) still flows in.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var json = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => t.MenuJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return DefaultLayout();
        try
        {
            var stored = JsonSerializer.Deserialize<MenuLayoutDto>(json, Json);
            if (stored is null) return DefaultLayout();
            return MergeWithCatalog(stored);
        }
        catch { return DefaultLayout(); }
    }

    public async Task SaveLayoutAsync(MenuLayout layout, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved) return;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct);
        if (row is null) return;
        var dto = new MenuLayoutDto(
            layout.Workspace.Select(x => new MenuItemDto(x.Id, x.Label, x.Order, x.Visible, x.Section)).ToList());
        row.MenuJson = JsonSerializer.Serialize(dto, Json);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ResolvedMenuItem>> GetWorkspaceMenuAsync(Guid? projectId, CancellationToken ct = default)
    {
        var layout = await GetLayoutAsync(ct);
        var perms = await SafePermsAsync(ct);
        var byId = WorkspaceCatalog.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var items = new List<ResolvedMenuItem>();
        foreach (var o in layout.Workspace.OrderBy(x => x.Order))
        {
            if (!o.Visible || !byId.TryGetValue(o.Id, out var cat)) continue;
            if (cat.Kind == "section")
            {
                items.Add(new ResolvedMenuItem(cat.Id, o.Label ?? cat.DefaultLabel, "section", null, null, o.Section ?? cat.DefaultSection, o.Order));
                continue;
            }
            if (cat.RequiredPermission is not null && !perms.Contains(cat.RequiredPermission)) continue;
            // Project-scoped links need a selected project.
            if (cat.HrefTemplate?.Contains("{projectId}", StringComparison.Ordinal) == true && projectId is null)
                continue;
            // Entity accordion groups only make sense inside a project context.
            if (cat.Kind == "entities" && projectId is null)
                continue;
            // Settings: show if any of settings/members/backup
            if (cat.Id == "settings" && !perms.Contains(Permission.SettingsManage)
                && !perms.Contains(Permission.MembersManage) && !perms.Contains(Permission.BackupManage))
                continue;
            if (cat.Id == "mcp" && !perms.Contains(Permission.ProjectsView) && !perms.Contains(Permission.JobsView))
                continue;

            var href = cat.HrefTemplate?
                .Replace("{projectId}", projectId?.ToString() ?? "", StringComparison.Ordinal);
            items.Add(new ResolvedMenuItem(
                cat.Id, o.Label ?? cat.DefaultLabel, cat.Kind, href, cat.Icon,
                o.Section ?? cat.DefaultSection, o.Order, cat.Parent));
        }
        return items;
    }

    private async Task<HashSet<string>> SafePermsAsync(CancellationToken ct)
    {
        try
        {
            var set = await _perms.GetEffectivePermissionsAsync(ct);
            return new HashSet<string>(set, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Ensure every catalog id appears once; unknown stored ids are dropped.</summary>
    private MenuLayout MergeWithCatalog(MenuLayoutDto stored)
    {
        return new MenuLayout(MergeSide(stored.Workspace, WorkspaceCatalog));
    }

    private static List<MenuItemOverride> MergeSide(List<MenuItemDto>? stored, CatalogItem[] catalog)
    {
        var knownIds = catalog.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var result = (stored ?? new())
            .Where(x => knownIds.Contains(x.Id))
            .OrderBy(x => x.Order)
            .DistinctBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new MenuItemOverride(x.Id, x.Label, x.Order, x.Visible, x.Section))
            .ToList();

        // Preserve customized order, but place newly shipped entries beside their nearest
        // catalog predecessor rather than appending them after the Workspace section.
        for (var catalogIndex = 0; catalogIndex < catalog.Length; catalogIndex++)
        {
            var item = catalog[catalogIndex];
            if (result.Any(x => x.Id == item.Id)) continue;

            var insertAt = result.Count;
            for (var previous = catalogIndex - 1; previous >= 0; previous--)
            {
                var previousIndex = result.FindIndex(x => x.Id == catalog[previous].Id);
                if (previousIndex < 0) continue;
                insertAt = previousIndex + 1;
                break;
            }
            if (insertAt == result.Count)
            {
                for (var next = catalogIndex + 1; next < catalog.Length; next++)
                {
                    var nextIndex = result.FindIndex(x => x.Id == catalog[next].Id);
                    if (nextIndex < 0) continue;
                    insertAt = nextIndex;
                    break;
                }
            }

            result.Insert(insertAt, new MenuItemOverride(item.Id, null, 0, true, item.DefaultSection));
        }

        return result.Select((item, index) => item with { Order = index * 10 }).ToList();
    }

    private sealed record MenuLayoutDto(List<MenuItemDto>? Workspace);
    private sealed record MenuItemDto(string Id, string? Label, int Order, bool Visible, string? Section);
}
