using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Branding;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class MainLayoutViewModel : PageViewModel, IDisposable
{
    private static class Routes
    {
        public const string Project = "/project/";
        public const string Entity = "/entity/";
        public const string Entities = "/entities";
        public const string Jobs = "/jobs";
        public const string WikiGettingStarted = "/wiki/getting-started";
    }

    public static class MenuKinds
    {
        public const string Section = "section";
        public const string Entities = "entities";
    }

    public static class MenuIds
    {
        public const string Mcp = "mcp";
    }

    public static class ThemeNames
    {
        public const string Dark = "dark";
        public const string Light = "light";
    }

    private static readonly Regex ProjectIdPattern = new(
        @"/project/([0-9a-fA-F-]{36})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private readonly PortalUiState _ui;
    private readonly IConfiguration _configuration;
    private readonly ICurrentTenant _tenant;
    private readonly OperationCenter _operations;
    private readonly IJSRuntime _js;
    private readonly NavigationManager _navigation;
    private readonly IServiceScopeFactory _scopeFactory;
    private IReadOnlyList<ProjectSummaryView> _projects = Array.Empty<ProjectSummaryView>();
    private IReadOnlyList<ResolvedMenuItem> _menu = Array.Empty<ResolvedMenuItem>();
    private IReadOnlyList<string> _entities = Array.Empty<string>();
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private Guid? _entitiesFor;
    private bool _focusSearch;
    private ElementReference _searchInput;

    public MainLayoutViewModel(
        PortalUiState ui,
        IConfiguration configuration,
        ICurrentTenant tenant,
        OperationCenter operations,
        IJSRuntime js,
        NavigationManager navigation,
        IServiceScopeFactory scopeFactory
    )
    {
        _ui = ui;
        _configuration = configuration;
        _tenant = tenant;
        _operations = operations;
        _js = js;
        _navigation = navigation;
        _scopeFactory = scopeFactory;
    }

    public TenantBranding Brand { get; private set; } = new();
    public string Theme { get; private set; } = ThemeNames.Dark;
    public bool SwitcherOpen { get; private set; }
    public bool NavOpen { get; private set; }
    public int RunningCount { get; private set; }
    public string OrganizationName =>
        string.IsNullOrEmpty(_tenant.Slug) ? "organisation" : _tenant.Slug;
    public string RootPath => _configuration["PlaceContext:RootPath"] ?? "/home/brad/code";
    public string Title => _ui.Title;
    public string Subtitle => _ui.Sub;
    public Guid? CurrentProjectId => _ui.CurrentProjectId;
    public string? CurrentProjectName => _ui.CurrentProjectName;
    public bool HasSubNav => _ui.HasSubNav;
    public IReadOnlyList<ProjectSummaryView> Projects => _projects;
    public IEnumerable<ResolvedMenuItem> TopLevel => _menu.Where(item => item.Parent is null);
    public IReadOnlyList<string> Entities => _entities;
    public bool SearchOpen { get; private set; }
    public bool Searching { get; private set; }
    public string SearchTerm { get; private set; } = string.Empty;
    public SearchResultsView? SearchResults { get; private set; }
    public string? SearchEmptyMessage =>
        SearchTerm.Trim().Length >= 2
            ? $"No matches for “{SearchTerm}”."
            : "Type at least 2 characters to search this workspace.";
    public string ThemeToggleLabel => Theme == ThemeNames.Dark ? "light" : "dark";
    public bool IsLightTheme => Theme == ThemeNames.Light;
    public string BrandCssOverrides => Brand.CssOverrides();
    public string ProductName => Brand.ProductName ?? "placecontext";
    public bool HasLogo => Brand.LogoDataUri is not null;

    public bool IsSection(ResolvedMenuItem item) => item.Kind == MenuKinds.Section;

    public bool IsEntitiesGroup(ResolvedMenuItem item) => item.Kind == MenuKinds.Entities;

    public bool IsMcp(ResolvedMenuItem item) => item.Id == MenuIds.Mcp;

    public bool IsDarkTheme => Theme == ThemeNames.Dark;
    public string ThemeSwitchLabel => IsDarkTheme ? ThemeNames.Light : ThemeNames.Dark;
    public bool IsThemeLightPressed => IsLightTheme;
    public string ThemeIcon =>
        IsDarkTheme
            ? "<svg width='15' height='15' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><circle cx='12' cy='12' r='4'></circle><path d='M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.5 1.5M17.5 17.5L19 19M19 5l-1.5 1.5M6.5 17.5L5 19'></path></svg>"
            : "<svg width='15' height='15' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><path d='M20.5 14.3A8.5 8.5 0 1 1 9.7 3.5 8.5 8.5 0 0 0 20.5 14.3Z'></path></svg>";

    public async Task InitializeAsync()
    {
        _ui.OnChanged += OnUiChanged;
        _operations.Changed += OnOperationsChanged;
        _navigation.LocationChanged += OnLocationChanged;
        _ui.SetMainNavOpener(OpenNavigation);
        try
        {
            Brand = await InScopeAsync<BrandingService, TenantBranding>(service =>
                service.GetAsync()
            );
        }
        catch { }
        try
        {
            _projects = await InScopeAsync<IPlaceContextService, IReadOnlyList<ProjectSummaryView>>(
                service => service.GetProjectsAsync()
            );
        }
        catch
        {
            _projects = Array.Empty<ProjectSummaryView>();
        }
        SyncProjectFromUrl(_navigation.Uri);
        if (_ui.CurrentProjectId is null && _projects.Count > 0)
            _ui.SetProject(_projects[0].Id, _projects[0].Name);
        await ReloadMenuAsync();
        RefreshRunningCount();
    }

    public async Task AfterRenderAsync(bool firstRender, ElementReference searchInput)
    {
        _searchInput = searchInput;
        if (firstRender)
        {
            Theme = NormalizeTheme(await _js.InvokeAsync<string>("placecontext.initTheme"));
            NotifyStateChanged();
        }
        await _js.InvokeVoidAsync("placecontext.animateMeters");
        if (_focusSearch)
        {
            _focusSearch = false;
            try
            {
                await _searchInput.FocusAsync();
            }
            catch { }
        }
    }

    public IEnumerable<ResolvedMenuItem> ChildrenOf(string parentId) =>
        _menu.Where(item => item.Parent == parentId).OrderBy(item => item.Order);

    public bool IsGroup(ResolvedMenuItem item) =>
        item.Kind == MenuKinds.Entities || ChildrenOf(item.Id).Any();

    public string? AbsoluteHref(string? href) =>
        string.IsNullOrEmpty(href) ? null
        : href.StartsWith('/') ? href
        : "/" + href;

    public NavLinkMatch MatchFor(string? href) =>
        href == "/" ? NavLinkMatch.All : NavLinkMatch.Prefix;

    public bool PathMatches(string? href) => PathMatches(GetCurrentPath(), href);

    public bool IsEntityActive(string entityName)
    {
        var path = Uri.UnescapeDataString(GetCurrentPath());
        var marker = Routes.Entity;
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;
        var segment = path[(index + marker.Length)..].TrimEnd('/');
        var slash = segment.IndexOf('/');
        if (slash >= 0)
            segment = segment[..slash];
        return segment.Equals(entityName, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsGroupActive(ResolvedMenuItem item)
    {
        var children = ChildrenOf(item.Id).ToList();
        if (PathMatches(item.Href))
            return true;
        if (children.Any(child => PathMatches(child.Href)))
            return true;
        return item.Kind == MenuKinds.Entities && IsOnEntityPath(GetCurrentPath());
    }

    public bool IsExpanded(string id) => _expanded.Contains(id);

    public void ToggleGroup(string id)
    {
        if (!_expanded.Remove(id))
            _expanded.Add(id);
    }

    public void ToggleSwitcher() => SwitcherOpen = !SwitcherOpen;

    public void CloseNavigation() => NavOpen = false;

    public void ToggleNavigation()
    {
        if (_ui.HasSubNav && _ui.ToggleSubNav is not null)
            _ui.ToggleSubNav();
        else
            NavOpen = !NavOpen;
    }

    public void OpenNavigation() => _ = InvokeOnDispatcherAsync(() => NavOpen = true);

    public async Task SwitchProjectAsync(ProjectSummaryView project)
    {
        SwitcherOpen = false;
        _ui.SetProject(project.Id, project.Name);
        await ReloadMenuAsync();
        var destination =
            _menu
                .FirstOrDefault(item =>
                    item.Href?.Contains($"{Routes.Project}{project.Id}", StringComparison.Ordinal)
                    == true
                )
                ?.Href
            ?? $"{Routes.Project}{project.Id}{Routes.Jobs}";
        _navigation.NavigateTo(destination);
    }

    public void OpenSearch()
    {
        SearchOpen = true;
        _focusSearch = true;
        SearchTerm = string.Empty;
        SearchResults = null;
    }

    public void CloseSearch() => SearchOpen = false;

    public async Task SearchAsync(string? input)
    {
        SearchTerm = input ?? string.Empty;
        var term = SearchTerm.Trim();
        if (term.Length < 2)
        {
            SearchResults = null;
            return;
        }
        Searching = true;
        try
        {
            SearchResults = await InScopeAsync<IPlaceContextService, SearchResultsView>(service =>
                service.SearchAsync(term, _ui.CurrentProjectId)
            );
        }
        catch
        {
            SearchResults = null;
        }
        finally
        {
            Searching = false;
            NotifyStateChanged();
        }
    }

    public void HandleSearchKey(string key)
    {
        if (key == "Escape")
            CloseSearch();
    }

    public void GoToSearchResult(SearchHit hit)
    {
        CloseSearch();
        _navigation.NavigateTo(hit.Url);
    }

    public void GoTo(string href)
    {
        NavOpen = false;
        _navigation.NavigateTo(href);
    }

    public string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";
        var parts = name.Split(
            new[] { ' ', '.', '@', '_', '-' },
            StringSplitOptions.RemoveEmptyEntries
        );
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant(),
        };
    }

    public string IconMarkup(string? kind) => MainLayoutIconCatalog.Markup(kind);

    public const string CaretMarkup =
        "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.2' stroke-linecap='round' stroke-linejoin='round'><path d='M9 6l6 6-6 6'></path></svg>";

    public async Task ToggleThemeAsync() =>
        Theme = NormalizeTheme(await _js.InvokeAsync<string>("placecontext.toggleTheme"));

    public void Dispose()
    {
        _ui.OnChanged -= OnUiChanged;
        _operations.Changed -= OnOperationsChanged;
        _navigation.LocationChanged -= OnLocationChanged;
        Detach();
    }

    private async Task ReloadMenuAsync()
    {
        try
        {
            var menu = await InScopeAsync<IMenuConfigService, IReadOnlyList<ResolvedMenuItem>>(
                service => service.GetWorkspaceMenuAsync(_ui.CurrentProjectId)
            );
            if (menu.Count > 0)
                _menu = menu;
        }
        catch { }
        await ReloadEntitiesAsync();
        ExpandActiveGroups();
        NotifyStateChanged();
    }

    private async Task ReloadEntitiesAsync()
    {
        var projectId = _ui.CurrentProjectId;
        if (projectId is null || !_menu.Any(item => item.Kind == MenuKinds.Entities))
        {
            _entities = Array.Empty<string>();
            _entitiesFor = null;
            return;
        }
        if (_entitiesFor == projectId)
            return;
        _entities = Array.Empty<string>();
        _entitiesFor = null;
        try
        {
            _entities = (
                await InScopeAsync<IPlaceContextService, IReadOnlyList<DataEntityView>>(service =>
                    service.ListDataEntitiesAsync(projectId.Value)
                )
            )
                .Select(item => item.Name)
                .ToList();
            _entitiesFor = projectId;
        }
        catch { }
    }

    private void ExpandActiveGroups()
    {
        foreach (var item in _menu.Where(IsGroup))
            if (IsGroupActive(item))
                _expanded.Add(item.Id);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        NavOpen = false;
        var before = _ui.CurrentProjectId;
        SyncProjectFromUrl(args.Location);
        _ = InvokeOnDispatcherAsync(async () =>
        {
            if (_ui.CurrentProjectId != before)
                await ReloadMenuAsync();
            else
                await ReloadEntitiesAsync();
            ExpandActiveGroups();
        });
    }

    private void SyncProjectFromUrl(string uri)
    {
        var match = ProjectIdPattern.Match(uri);
        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var projectId))
            return;
        var project = _projects.FirstOrDefault(item => item.Id == projectId);
        if (project is not null)
            _ui.SetProject(project.Id, project.Name);
    }

    private void OnOperationsChanged() => _ = InvokeOnDispatcherAsync(RefreshRunningCount);

    private void OnUiChanged() => _ = InvokeOnDispatcherAsync(static () => { });

    private void RefreshRunningCount() =>
        RunningCount = _tenant.IsResolved ? _operations.ActiveCount(_tenant.TenantId) : 0;

    private string GetCurrentPath() => new Uri(_navigation.Uri).AbsolutePath.TrimEnd('/');

    private static bool IsOnEntityPath(string path) =>
        path.Contains(Routes.Entity, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(Routes.Entities, StringComparison.OrdinalIgnoreCase);

    private static bool PathMatches(string path, string? href)
    {
        if (string.IsNullOrEmpty(href))
            return false;
        var target = href.TrimEnd('/');
        if (target.Length == 0)
            target = "/";
        if (path.Equals(target, StringComparison.OrdinalIgnoreCase))
            return true;
        return target != "/" && path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTheme(string? theme) =>
        string.Equals(theme, ThemeNames.Light, StringComparison.OrdinalIgnoreCase)
            ? ThemeNames.Light
            : ThemeNames.Dark;

    private async Task<TResult> InScopeAsync<TService, TResult>(
        Func<TService, Task<TResult>> operation
    )
        where TService : notnull
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private Task InvokeOnDispatcherAsync(Action action)
    {
        action();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    private async Task InvokeOnDispatcherAsync(Func<Task> action)
    {
        await action();
        NotifyStateChanged();
    }
}

internal static class MainLayoutIconCatalog
{
    public static string Markup(string? kind)
    {
        var resolved = kind?.Trim().ToLowerInvariant();
        return resolved switch
        {
            "grid" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7'><rect x='3' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='14' width='7' height='7' rx='1.5'></rect><rect x='3' y='14' width='7' height='7' rx='1.5'></rect></svg>",
            "dashboard" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7'><rect x='3' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='3' width='7' height='7' rx='1.5'></rect><rect x='14' y='14' width='7' height='7' rx='1.5'></rect><rect x='3' y='14' width='7' height='7' rx='1.5'></rect></svg>",
            "rocket" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor'><path d='M9 11a14 14 0 0 1 7-8c2.6 0 4 1.4 4 4a14 14 0 0 1-8 7l-3-3z'></path></svg>",
            "users" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M7 21a4 4 0 0 1 4-4h2a4 4 0 0 1 4 4'></path><circle cx='9' cy='8' r='3'></circle><path d='M17 11a3 3 0 0 1 0 6'></path><circle cx='15' cy='8' r='3'></circle></svg>",
            "box" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M4 7l8-4 8 4-8 4-8-4Z'></path><path d='M4 7v10l8 4 8-4V7'></path><path d='M12 11v10'></path></svg>",
            "test" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M9 2h6'></path><path d='M10 22h4'></path><path d='M12 9v3'></path><path d='M9 6l-3 3 3 3'></path><path d='M15 6l3 3-3 3'></path><path d='M12 14v7'></path><path d='M6 12h12'></path></svg>",
            "chain" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M4 12h3.5'></path><path d='M9 12h6'></path><path d='M16.5 12h3.5'></path><path d='M8 9h8L16 7V5m-4 10h0v5m0-5 4-2.5m0 0 4 2.5m-4-2.5-4 2.5M4 12h.5'></path><path d='M6.5 8.5h3m-3 7h3'></path><path d='M14.5 9h3m-3 6h3'></path></svg>",
            "crm" or "users" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M12 13.5c3 0 5.5 2.5 5.5 5.5v1H6.5v-1c0-3 2.5-5.5 5.5-5.5Z'></path><circle cx='12' cy='8' r='3.2'></circle></svg>",
            "clock" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><circle cx='12' cy='12' r='9'></circle><polyline points='12 7 12 12 15 15'></polyline></svg>",
            "map" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 6l6.5-2.5L15 6l5.5-2.5V19L15 21.5 9.5 19 3 21.5z'></path><path d='M15 6v16'></path><path d='M3.4 6.2L9.5 9l5.5-2.8'></path><path d='M9.5 9v13'></path></svg>",
            "key" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><circle cx='7.5' cy='16.5' r='2.5'></circle><path d='M10 16.5h9.5'></path><path d='M19.5 16.5l-1.1-1.1-2.2-2.2-1.8-1.8'></path><path d='M16.5 13.5 14 11a3 3 0 1 0-4.2 4.2'></path></svg>",
            "pulse" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 12h4l2-4 3 8 2-4 3 4h7'></path></svg>",
            "chat" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M21 15a4 4 0 0 1-4 4H7l-4 3v-18a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4z'></path></svg>",
            "file" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M14 2H7.5A1.5 1.5 0 0 0 6 3.5V20.5A1.5 1.5 0 0 0 7.5 22h9A1.5 1.5 0 0 0 18 20.5V8z'></path><path d='M14 2v6h6'></path><path d='M8 10h8'></path><path d='M8 14h8'></path><path d='M8 18h6'></path></svg>",
            "ledger" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M4 4h16'></path><path d='M4 8h16'></path><path d='M4 12h16'></path><path d='M4 16h16'></path><path d='M4 20h16'></path><path d='M8 4v16'></path><path d='M16 4v16'></path></svg>",
            "data" or "data.tables" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><path d='M4 7h16M4 11h16M4 15h16M4 19h16'/></svg>",
            "data.analytics" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><path d='M4 19h16M7 19V5M11 19V8M15 19V11M4 16h16'/></svg>",
            "data.search" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><circle cx='11' cy='11' r='7'></circle><path d='M20 20l-3.5-3.5'/></svg>",
            "data.datamap" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><path d='M4 4h16M4 20h16M8 4v16M12 4v16M16 4v16'/></svg>",
            "data.entities" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><circle cx='9' cy='8' r='3'></circle><circle cx='16' cy='8' r='3'></circle><path d='M4 21a4 4 0 0 1 4-4h8a4 4 0 0 1 4 4'></path><path d='M2 19.5c0-4 4-5 7-5s7 1 7 5'></path><path d='M9 21a7 7 0 0 1 14 0'></path></svg>",
            "data.graph" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><circle cx='5' cy='12' r='3'></circle><circle cx='19' cy='5' r='3'></circle><circle cx='19' cy='19' r='3'></circle><path d='M8 10.5l8-4M8 13.5l8 4'></path></svg>",
            "observability" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 3v18h18M7 15v-4M12 15V9M17 15V11'></path></svg>",
            "overview" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M3 4h18M3 8h18M3 12h18M3 16h18M3 20h18'></path></svg>",
            "wiki" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M6 4h8l4 4v12H6z'></path><path d='M14 4v4h4M9 12h6'/></svg>",
            "about" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><circle cx='12' cy='12' r='9'/><path d='M12 10h.01M12 14h.01M9 8h6'/></svg>",
            "settings" =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'><path d='M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z'></path><path d='M19.4 15a1.8 1.8 0 0 0 .4-1.1v-.9a1.8 1.8 0 0 0-.4-1.1l-1.5-1.2a1.8 1.8 0 0 0-.2-2l.6-1.8a1.8 1.8 0 0 0-1-2h-1.6a1.8 1.8 0 0 0-1.3.3l-1.9 1.2a1.8 1.8 0 0 0-1.9 0l-1.9-1.2a1.8 1.8 0 0 0-1.3-.3h-1.6a1.8 1.8 0 0 0-1 2l.6 1.8a1.8 1.8 0 0 0-.2 2l-1.5 1.2A1.8 1.8 0 0 0 4 12.9v1.8a1.8 1.8 0 0 0 .4 1.1l1.5 1.2a1.8 1.8 0 0 0 .2 2l-.6 1.8a1.8 1.8 0 0 0 1 2h1.6a1.8 1.8 0 0 0 1.3-.3l1.9-1.2a1.8 1.8 0 0 0 1.9 0l1.9 1.2a1.8 1.8 0 0 0 1.3.3h1.6a1.8 1.8 0 0 0 1-2l-.6-1.8a1.8 1.8 0 0 0 .2-2z'/></svg>",
            _ =>
                "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor'><rect x='3' y='4' width='18' height='16' rx='2'></rect><path d='M7 9l3 2.5L7 14'></path><path d='M13 14.5h4'></path></svg>",
        };
    }
}
