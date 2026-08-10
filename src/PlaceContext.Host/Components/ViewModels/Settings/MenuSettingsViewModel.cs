using PlaceContext.Application.Ports;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class MenuSettingsViewModel(IMenuConfigService menu, PortalUiState ui) : PageViewModel
{
    public sealed class Row
    {
        public string Id { get; init; } = "";
        public string DefaultLabel { get; init; } = "";
        public string Label { get; set; } = "";
        public int Order { get; set; }
        public bool Visible { get; set; } = true;
        public string Section { get; set; } = "";
    }

    private static readonly IReadOnlyDictionary<string, string> CatalogLabels = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["dashboard"] = "Dashboard",
        ["jobs"] = "Jobs",
        ["tests"] = "Tests",
        ["chains"] = "Chains",
        ["schedules"] = "Schedules",
        ["data"] = "Data",
        ["project.entities"] = "Business",
        ["project.entities.registry"] = "Entities",
        ["vault"] = "Vault",
        ["project.events"] = "Events",
        ["agents"] = "Agents",
        ["chat"] = "Chat",
        ["artifacts"] = "Artifacts",
        ["observability"] = "Observability",
        ["sec-workspace"] = "Workspace (section)",
        ["wiki"] = "Wiki",
        ["settings"] = "Settings",
        ["about"] = "About",
    };

    public List<Row> Workspace { get; private set; } = [];
    public bool Saving { get; private set; }
    public string? Message { get; private set; }

    public async Task LoadAsync()
    {
        ui.Set("Settings", "Menu");
        var layout = await menu.GetLayoutAsync();
        Workspace = MergeRows(menu.DefaultLayout().Workspace, layout.Workspace);
        NotifyStateChanged();
    }

    public void Move(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= Workspace.Count)
            return;
        (Workspace[index], Workspace[target]) = (Workspace[target], Workspace[index]);
        Renumber(Workspace);
        NotifyStateChanged();
    }

    public void Reset()
    {
        var defaults = menu.DefaultLayout();
        Workspace = MergeRows(defaults.Workspace, defaults.Workspace);
        NotifyStateChanged();
    }

    public async Task SaveAsync()
    {
        Saving = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            Renumber(Workspace);
            await menu.SaveLayoutAsync(
                new MenuLayout(
                    Workspace
                        .Select(row => new MenuItemOverride(
                            row.Id,
                            string.IsNullOrWhiteSpace(row.Label) ? null : row.Label.Trim(),
                            row.Order,
                            row.Visible,
                            string.IsNullOrWhiteSpace(row.Section) ? null : row.Section.Trim()
                        ))
                        .ToList()
                )
            );
            Message = "Menu saved. Reload the page to refresh the sidebars.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public static List<Row> MergeRows(
        IReadOnlyList<MenuItemOverride> defaults,
        IReadOnlyList<MenuItemOverride> current
    )
    {
        var byId = current.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return defaults
            .OrderBy(item => item.Order)
            .Select(item =>
            {
                byId.TryGetValue(item.Id, out var currentItem);
                CatalogLabels.TryGetValue(item.Id, out var label);
                return new Row
                {
                    Id = item.Id,
                    DefaultLabel = label ?? item.Id,
                    Label = currentItem?.Label ?? item.Label ?? "",
                    Order = currentItem?.Order ?? item.Order,
                    Visible = currentItem?.Visible ?? item.Visible,
                    Section = currentItem?.Section ?? item.Section ?? "",
                };
            })
            .OrderBy(row => row.Order)
            .ToList();
    }

    private static void Renumber(List<Row> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            rows[i].Order = i * 10;
    }
}
