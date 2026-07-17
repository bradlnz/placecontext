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
        new("dashboard", "Dashboard", "link", "/", null, Permission.ProjectsView, "grid", null),
        new("jobs", "Jobs", "link", "/project/{projectId}/jobs", null, Permission.JobsView, "box", null),
        new("chains", "Chains", "link", "/project/{projectId}/chains", null, Permission.JobsView, "chain", null),
        new("schedules", "Schedules", "link", "/project/{projectId}/schedules", null, Permission.JobsView, "clock", null),
        new("data", "Data", "link", "/project/{projectId}/data", null, Permission.DataRead, "map", null),
        new("vault", "Vault", "link", "/project/{projectId}/secrets", null, Permission.SecretsManage, "key", null),
        new("artifacts", "Artifacts", "link", "/artifacts", null, Permission.ArtifactsView, "file", null),
        new("observability", "Observability", "link", "/observability", null, Permission.JobsView, "pulse", null),
        new("cluster", "Cluster", "link", "/cluster", null, Permission.SettingsManage, "grid", null),
        new("mcp", "MCP Server", "link", "/inspector", null, Permission.ProjectsView, "inspector", null),
        new("sec-workspace", "Workspace", "section", null, null, null, null, "Workspace"),
        new("overview", "Projects overview", "link", "/overview", null, Permission.ProjectsView, "pulse", "Workspace"),
        new("onboarding", "Onboarding", "link", "/onboarding", null, Permission.ProjectsView, "rocket", "Workspace"),
        new("wiki", "Wiki", "link", "/wiki", null, null, "ledger", "Workspace"),
        new("settings", "Settings", "link", "/settings/branding", null, Permission.SettingsManage, "key", "Workspace"),
        new("about", "About", "link", "/about", null, null, "grid", "Workspace"),
    };

    internal static readonly CatalogItem[] ProjectCatalog =
    {
        new("project.overview", "Overview", "link", null, "", Permission.ProjectsView, null, null),
        new("project.jobs", "Jobs", "link", null, "jobs", Permission.JobsView, null, null),
        new("project.chains", "Chains", "link", null, "chains", Permission.JobsView, null, null),
        new("project.schedules", "Schedules", "link", null, "schedules", Permission.JobsView, null, null),
        new("project.data", "Data", "link", null, "data", Permission.DataRead, null, null),
        new("project.vault", "Vault", "link", null, "secrets", Permission.SecretsManage, null, null),
        new("project.events", "Events", "link", null, "events", Permission.EventsManage, null, null),
        new("project.entities", "Business", "entities", null, null, Permission.DataRead, null, "Business"),
    };

    internal sealed record CatalogItem(
        string Id, string DefaultLabel, string Kind, string? HrefTemplate, string? ProjectPath,
        string? RequiredPermission, string? Icon, string? DefaultSection);

    public MenuLayout DefaultLayout()
    {
        var ws = WorkspaceCatalog.Select((c, i) => new MenuItemOverride(
            c.Id, null, i * 10, true, c.DefaultSection)).ToList();
        var pr = ProjectCatalog.Select((c, i) => new MenuItemOverride(
            c.Id, null, i * 10, true, c.DefaultSection)).ToList();
        return new MenuLayout(ws, pr);
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
            layout.Workspace.Select(x => new MenuItemDto(x.Id, x.Label, x.Order, x.Visible, x.Section)).ToList(),
            layout.Project.Select(x => new MenuItemDto(x.Id, x.Label, x.Order, x.Visible, x.Section)).ToList());
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
                o.Section ?? cat.DefaultSection, o.Order));
        }
        return items;
    }

    public async Task<IReadOnlyList<ResolvedMenuItem>> GetProjectMenuAsync(Guid projectId, CancellationToken ct = default)
    {
        var layout = await GetLayoutAsync(ct);
        var perms = await SafePermsAsync(ct);
        var byId = ProjectCatalog.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var items = new List<ResolvedMenuItem>();
        foreach (var o in layout.Project.OrderBy(x => x.Order))
        {
            if (!o.Visible || !byId.TryGetValue(o.Id, out var cat)) continue;
            if (cat.RequiredPermission is not null && !perms.Contains(cat.RequiredPermission)) continue;
            string? href = cat.Kind == "entities"
                ? null
                : cat.ProjectPath is { Length: 0 }
                    ? $"/project/{projectId}"
                    : $"/project/{projectId}/{cat.ProjectPath}";
            items.Add(new ResolvedMenuItem(
                cat.Id, o.Label ?? cat.DefaultLabel, cat.Kind, href, cat.Icon,
                o.Section ?? cat.DefaultSection, o.Order));
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
        var ws = MergeSide(stored.Workspace, WorkspaceCatalog);
        var pr = MergeSide(stored.Project, ProjectCatalog);
        return new MenuLayout(ws, pr);
    }

    private static List<MenuItemOverride> MergeSide(List<MenuItemDto>? stored, CatalogItem[] catalog)
    {
        var map = (stored ?? new()).Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var result = new List<MenuItemOverride>();
        var order = 0;
        // Keep stored order first for known ids
        foreach (var s in (stored ?? new()).OrderBy(x => x.Order))
        {
            if (catalog.All(c => c.Id != s.Id)) continue;
            result.Add(new MenuItemOverride(s.Id, s.Label, s.Order, s.Visible, s.Section));
            order = Math.Max(order, s.Order + 10);
        }
        // Append any new catalog items not in stored layout
        foreach (var c in catalog)
        {
            if (result.Any(r => r.Id == c.Id)) continue;
            result.Add(new MenuItemOverride(c.Id, null, order, true, c.DefaultSection));
            order += 10;
        }
        return result;
    }

    private sealed record MenuLayoutDto(List<MenuItemDto>? Workspace, List<MenuItemDto>? Project);
    private sealed record MenuItemDto(string Id, string? Label, int Order, bool Visible, string? Section);
}
