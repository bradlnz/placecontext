using Microsoft.AspNetCore.Components;
using PlaceContext.Host.Components.Models;
using PlaceContext.Application.Access;
using PlaceContext.Application.Tenancy;
namespace PlaceContext.Host.Components.ViewModels;

public sealed class SettingsLayoutViewModel : PageViewModel
{
    public const string ApiTokensPath = "api-tokens";
    private static readonly IReadOnlyList<SettingsItem> Items =
    [
        new("Workspace", "Branding", "branding"),
        new("Workspace", "Navigation", "menu"),
        new("Workspace", "Timezone", "locality"),
        new("Workspace", "Billing & usage", "billing"),
        new("Execution", "Cluster", "cluster"),
        new("Providers", "Data connections", "connections"),
        new("Providers", "Models", "models"),
        new("Providers", "MCP servers", "mcp"),
        new("Providers", "Communications", "communications"),
        new("Access", "Members & roles", "access"),
        new("Access", "API tokens", ApiTokensPath),
        new("Maintenance", "Artifact filters", "artifacts"),
        new("Maintenance", "Backup", "backup"),
    ];
    private readonly NavigationManager _navigation;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;

    public SettingsLayoutViewModel(
        NavigationManager navigation,
        IServiceScopeFactory scopeFactory,
        ICurrentUser currentUser
    )
    {
        _navigation = navigation;
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
    }

    public bool NavigationOpen
    {
        get; private set;
    }
    public IEnumerable<SettingsItem> VisibleItems => Items.Where(item => IsDefaultAdmin || item.Path == ApiTokensPath);
    public IEnumerable<IGrouping<string, SettingsItem>> VisibleGroups => VisibleItems.GroupBy(item => item.Group);
    public string ActiveLabel =>
        VisibleItems.FirstOrDefault(item => IsActive(item.Path))?.Label ?? "Choose a section";
    public bool IsDefaultAdmin
    {
        get; private set;
    }

    public void ToggleNavigation() => NavigationOpen = !NavigationOpen;

    public void CloseNavigation() => NavigationOpen = false;

    public bool IsActive(string path)
    {
        var currentPath = new Uri(_navigation.Uri).AbsolutePath;
        return currentPath.Equals($"/settings/{path}", StringComparison.OrdinalIgnoreCase)
            || path == "cluster" && currentPath.Equals("/cluster", StringComparison.OrdinalIgnoreCase);
    }

    public string Href(string path) => $"/settings/{path}";

    public async Task InitializeAsync()
    {
        IsDefaultAdmin = false;
        if (_currentUser.IsAuthenticated)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            IsDefaultAdmin = await scope
                .ServiceProvider.GetRequiredService<IPermissionService>()
                .HasAsync(Permission.SettingsManage);
        }
        NotifyStateChanged();
    }

}
