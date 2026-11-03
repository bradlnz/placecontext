using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Access;
using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Application.Tenancy;
namespace PlaceContext.Host.Components.ViewModels;

public sealed class SettingsLayoutViewModel : PageViewModel
{
    public const string ApiTokensPath = "api-tokens";
    private static readonly IReadOnlyList<SettingsItem> Items =
    [
        new("Cluster", "cluster"),
        new("PlaceContext database", "database"),
        new("Branding", "branding"),
        new("Menu", "menu"),
        new("Artifacts", "artifacts"),
        new("Communications", "communications"),
        new("Models", "models"),
        new("Connections", "connections"),
        new("Billing & usage", "billing"),
        new("MCP servers", "mcp"),
        new("Locality", "locality"),
        new("Backup", "backup"),
        new("Access", "access"),
        new("API tokens", ApiTokensPath),
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
    public bool CanInspectDatabase { get; private set; }
    public IEnumerable<SettingsItem> VisibleItems => Items.Where(item => item.Path == "database"
        ? CanInspectDatabase : IsDefaultAdmin || item.Path == ApiTokensPath);
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
        CanInspectDatabase = false;
        if (_currentUser.IsAuthenticated)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            IsDefaultAdmin = await scope
                .ServiceProvider.GetRequiredService<IPermissionService>()
                .HasAsync(Permission.SettingsManage);
            CanInspectDatabase = await scope.ServiceProvider.GetRequiredService<DatabaseInspectionAccess>().CanReadAsync();
        }
        NotifyStateChanged();
    }

    public sealed record SettingsItem(string Label, string Path);
}
