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
    public string RootPath =>
        _configuration["PlaceContext:RootPath"] ?? Directory.GetCurrentDirectory();
    public string Title => ActiveNavigationTitle() ?? _ui.Title;
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
            ? "<svg class='shell-control-icon' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'><circle cx='12' cy='12' r='4'/><path d='M12 2v2M12 20v2M4.93 4.93l1.42 1.42M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41'/></svg>"
            : "<svg class='shell-control-icon' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'><path d='M21 12.8A8.5 8.5 0 1 1 11.2 3 6.6 6.6 0 0 0 21 12.8Z'/></svg>";

    public async Task InitializeAsync()
    {
        _ui.OnChanged += OnUiChanged;
        _operations.Changed += OnOperationsChanged;
        _navigation.LocationChanged += OnLocationChanged;
        _ui.SetMainNavOpener(OpenNavigation);
        var brandTask = InScopeAsync<BrandingService, TenantBranding>(service =>
            service.GetAsync()
        );
        var projectsTask = InScopeAsync<PlaceContextService, IReadOnlyList<ProjectSummaryView>>(
            service => service.GetProjectsAsync()
        );
        try
        {
            Brand = await brandTask;
        }
        catch { }
        try
        {
            _projects = await projectsTask;
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

    public async Task AfterRenderAsync(
        bool firstRender,
        ElementReference searchInput
    )
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
            SearchResults = await InScopeAsync<PlaceContextService, SearchResultsView>(service =>
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
        "<svg class='nav-caret-svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'><path d='m9 18 6-6-6-6'/></svg>";

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
                await InScopeAsync<PlaceContextService, IReadOnlyList<DataEntityView>>(service =>
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

    private string? ActiveNavigationTitle()
    {
        var path = GetCurrentPath();
        if (IsOnEntityPath(path))
        {
            var decodedPath = Uri.UnescapeDataString(path);
            var markerIndex = decodedPath.IndexOf(
                Routes.Entity,
                StringComparison.OrdinalIgnoreCase
            );
            if (markerIndex >= 0)
            {
                var entityName = decodedPath[(markerIndex + Routes.Entity.Length)..].TrimEnd('/');
                var slashIndex = entityName.IndexOf('/');
                if (slashIndex >= 0)
                    entityName = entityName[..slashIndex];
                if (!string.IsNullOrWhiteSpace(entityName))
                    return entityName;
            }
        }

        return _menu
            .Where(item => !IsSection(item) && PathMatches(path, AbsoluteHref(item.Href)))
            .OrderByDescending(item => AbsoluteHref(item.Href)?.Length ?? 0)
            .Select(item => item.Label)
            .FirstOrDefault();
    }

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
