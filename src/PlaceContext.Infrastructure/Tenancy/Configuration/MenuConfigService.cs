using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Organization;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Application.Access;

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
    private readonly ICurrentUser _currentUser;

    public MenuConfigService(IServiceScopeFactory scopeFactory, ICurrentTenant tenant, IPermissionService perms, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _tenant = tenant;
        _perms = perms;
        _currentUser = currentUser;
    }

    /// <summary>Public navigation catalog. Retired store/dashboard ids are intentionally absent;
    /// existing tenant overrides are dropped by MergeSide without deleting persisted user data.</summary>
    internal static readonly CatalogItem[] WorkspaceCatalog =
    {
        new("build.chat", "Dashboard", "link", "/project/{projectId}/dashboard", Permission.ProjectsView, "dashboard", null, null),
        new("project.agents", "Agents", "link", "/project/{projectId}/agents", Permission.ProjectsView, "agent", null, null),
        new("chains", "Workflows", "link", "/project/{projectId}/workflows", Permission.ChainsManage, "chain", null, null),
        new("jobs", "Jobs", "link", "/project/{projectId}/jobs", Permission.JobsView, "box", "Automation", null),
        new("tests", "Tests", "link", "/project/{projectId}/tests", Permission.JobsView, "test", "Automation", null),
        new("queues", "Queues", "link", "/project/{projectId}/queues", Permission.ChainsManage, "queue", "Infrastructure", null),
        new("networking", "Networking", "link", "/project/{projectId}/networking", Permission.SettingsManage, "box", "Infrastructure", null),
        new("schedules", "Schedules", "link", "/project/{projectId}/schedules", Permission.TriggersManage, "clock", "Automation", null),
        new("data", "Data", "link", "/project/{projectId}/data", Permission.DataRead, "data", null, null),
        new("vault", "Vault", "link", "/project/{projectId}/secrets", Permission.SecretsManage, "key", "Infrastructure", null),
        new("project.events", "Events", "link", "/project/{projectId}/events", Permission.EventsManage, "pulse", "Automation", null),
        new("artifacts", "Artifacts", "link", "/artifacts", Permission.ArtifactsView, "file", null, null),
        new("observability", "Observability", "link", "/observability", Permission.JobsView, "pulse", null, null),
        new("cluster", "Cluster", "link", "/cluster", Permission.SettingsManage, "box", "Infrastructure", null),
        new("wiki", "Wiki", "link", "/wiki", null, "ledger", "Help", null),
        new("settings", "Settings", "link", "/settings/branding", Permission.SettingsManage, "key", null, null),
        new("about", "About", "link", "/about", null, "grid", "Help", null),
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
        if (!_tenant.IsResolved)
        {
            return DefaultLayout();
        }        // Own scope (short-lived AppDbContext): this runs from MainLayout/ProjectLayout render,
        // concurrently with the page's own load on the circuit-shared scoped context. Reading the
        // tenant row on that shared context races the page ("A second operation was started on this
        // context instance"). Mirror PermissionService — the ambient tenant (AsyncLocal) still flows in.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var json = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => t.MenuJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return DefaultLayout();
        }
        try
        {
            var stored = JsonSerializer.Deserialize<MenuLayoutDto>(json, Json);
            if (stored is null)
            {
                return DefaultLayout();
            }
            return MergeWithCatalog(stored);
        }
        catch
        {
            return DefaultLayout();
        }
    }

    public async Task SaveLayoutAsync(MenuLayout layout, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
        {
            return;
        }
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct);
        if (row is null)
        {
            return;
        }
        var dto = new MenuLayoutDto(
                    layout.Workspace.Select(x => new MenuItemDto(x.Id, x.Label, x.Order, x.Visible, x.Section)).ToList());
        row.MenuJson = JsonSerializer.Serialize(dto, Json);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ResolvedMenuItem>> GetWorkspaceMenuAsync(Guid? projectId, CancellationToken ct = default)
    {
        var layout = await GetLayoutAsync(ct);
        var perms = await SafePermsAsync(ct);
        var isDefaultAdmin = await SafeIsDefaultAdminAsync(ct);
        var byId = WorkspaceCatalog.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var items = new List<ResolvedMenuItem>();
        foreach (var o in layout.Workspace.OrderBy(x => x.Order))
        {
            if (!o.Visible || !byId.TryGetValue(o.Id, out var cat))
            {
                continue;
            }
            if (cat.Kind == "section")
            {
                var section = NormalizeSection(o.Section) ?? NormalizeSection(cat.DefaultSection);
                if (string.IsNullOrWhiteSpace(section) && string.IsNullOrWhiteSpace(o.Label))
                {
                    continue;
                }
                items.Add(new ResolvedMenuItem(cat.Id, o.Label ?? cat.DefaultLabel, "section", null, null, section, o.Order));
                continue;
            }
            if (cat.RequiredPermission is not null && !perms.Contains(cat.RequiredPermission))
            {
                continue;
            }            // Project-scoped links need a selected project.
            if (cat.HrefTemplate?.Contains("{projectId}", StringComparison.Ordinal) == true && projectId is null)
            {
                continue;
            }            // Entity accordion groups only make sense inside a project context.
            if (cat.Kind == "entities" && projectId is null)
            {
                continue;
            }            // Settings: default admin only (the /settings/* area beyond the self-service API tokens
            // page is gated by the DefaultAdmin policy), and only when they hold one of the
            // settings-ish permissions.
            if (cat.Id == "settings" && (!isDefaultAdmin
                || (!perms.Contains(Permission.SettingsManage)
                    && !perms.Contains(Permission.MembersManage) && !perms.Contains(Permission.BackupManage))))
            {
                continue;
            }
            if (cat.Id == "mcp" && !perms.Contains(Permission.ProjectsView) && !perms.Contains(Permission.JobsView))
            {
                continue;
            }
            var href = cat.HrefTemplate?
                .Replace("{projectId}", projectId?.ToString() ?? "", StringComparison.Ordinal);
            items.Add(new ResolvedMenuItem(
                cat.Id, o.Label ?? cat.DefaultLabel, cat.Kind, href, cat.Icon,
                (cat.Id == "chains" && o.Section == "Automation")
                    || (cat.Id is "data" or "artifacts" && o.Section == "Data & storage")
                    ? null : NormalizeSection(o.Section) ?? NormalizeSection(cat.DefaultSection), o.Order, cat.Parent));
        }
        return GroupSections(items);
    }

    internal static IReadOnlyList<ResolvedMenuItem> GroupSections(IEnumerable<ResolvedMenuItem> items)
    {
        var result = new List<ResolvedMenuItem>();
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Parent is not null || item.Kind != "link" || string.IsNullOrWhiteSpace(item.Section))
            {
                result.Add(item);
                continue;
            }
            var id = "section:" + item.Section;
            if (groups.Add(id))
            {
                var icon = item.Section switch
                {
                    "Automation" => "chain",
                    "Infrastructure" => "box",
                    "Data & storage" => "data",
                    "Help" => "ledger",
                    _ => item.Icon,
                };
                result.Add(new(id, item.Section, "group", null, icon, null, item.Order));
            }
            result.Add(item with
            {
                Parent = id
            });
        }
        return result;
    }

    private static string? NormalizeSection(string? section) =>
        string.Equals(section?.Trim(), "Workspace", StringComparison.OrdinalIgnoreCase)
            ? null
            : section;

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

    /// <summary>Whether the current caller is the tenant's default admin. Same isolated-scope pattern
    /// as <see cref="GetLayoutAsync"/> — runs during layout render on the circuit.</summary>
    private async Task<bool> SafeIsDefaultAdminAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return false;
        }
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Users.AsNoTracking()
                .Where(u => u.Id == _currentUser.UserId)
                .Select(u => u.IsDefaultAdmin || u.IsExternalAdmin)
                .FirstOrDefaultAsync(ct);
        }
        catch
        {
            return false;
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
            .Select(x => new MenuItemOverride(x.Id,
                x.Id == "build.chat" && x.Label is "Chat" or "Build" ? null : x.Label,
                x.Order, x.Visible, x.Id == "build.chat" && x.Section == "Build" ? null : x.Section))
            .ToList();

        // Preserve customized order, but place newly shipped entries beside their nearest
        // catalog predecessor rather than appending them after the Workspace section.
        for (var catalogIndex = 0; catalogIndex < catalog.Length; catalogIndex++)
        {
            var item = catalog[catalogIndex];
            if (result.Any(x => x.Id == item.Id))
            {
                continue;
            }
            var insertAt = result.Count;
            for (var previous = catalogIndex - 1; previous >= 0; previous--)
            {
                var previousIndex = result.FindIndex(x => x.Id == catalog[previous].Id);
                if (previousIndex < 0)
                {
                    continue;
                }
                insertAt = previousIndex + 1;
                break;
            }
            if (insertAt == result.Count)
            {
                for (var next = catalogIndex + 1; next < catalog.Length; next++)
                {
                    var nextIndex = result.FindIndex(x => x.Id == catalog[next].Id);
                    if (nextIndex < 0)
                    {
                        continue;
                    }
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
