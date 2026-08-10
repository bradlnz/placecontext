using System.Globalization;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class OverviewViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;

    public OverviewViewModel(
        IPlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation
    ) => (_service, _ui, _navigation) = (service, ui, navigation);

    public IReadOnlyList<ProjectSummaryView>? Projects { get; private set; }
    public FocusView? Focus { get; private set; }
    public IReadOnlyList<Stat> Stats { get; private set; } = Array.Empty<Stat>();
    public bool Busy { get; private set; }

    public sealed record Stat(string Label, string Value, string Sub, string Color);

    public async Task LoadAsync()
    {
        Projects = await _service.GetProjectsAsync();
        Focus = await _service.GetFocusAsync();
        var stats = await _service.GetRootStatsAsync();
        Stats =
        [
            new(
                "Projects",
                stats.ProjectCount.ToString(CultureInfo.InvariantCulture),
                "under root",
                "var(--text)"
            ),
            new(
                "Changes today",
                stats.ChangesToday.ToString(CultureInfo.InvariantCulture),
                $"{stats.AgentChangesToday} agent · {stats.HumanChangesToday} human",
                "var(--text)"
            ),
            new(
                "God-nodes",
                stats.GodNodeTotal.ToString(CultureInfo.InvariantCulture),
                "top-degree files",
                "var(--text)"
            ),
            new(
                "Stale context",
                stats.StaleContextCount.ToString(CultureInfo.InvariantCulture),
                "need re-index",
                stats.StaleContextCount > 0 ? "var(--warn)" : "var(--good)"
            ),
        ];
        _ui.Set("Overview", "codebase visibility · projects register via MCP");
        NotifyStateChanged();
    }

    public async Task RefreshAsync()
    {
        Busy = true;
        NotifyStateChanged();
        try
        {
            await LoadAsync();
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public void Open(Guid projectId) => _navigation.NavigateTo($"/project/{projectId}/jobs");

    public void OpenFocus(string url) => _navigation.NavigateTo(url);

    public static string LanguageColor(string path) => Viz.LangColor(path);

    public static string Number(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    public static IEnumerable<(double X, double Y, bool God)> MiniNodes(ProjectSummaryView project)
    {
        var count = Math.Min(22, project.GodNodeCount * 3 + 10);
        var positions = Viz.Layout(count, Viz.Seed(project.Id), 100, 76);
        return positions.Select(
            (position, index) => (position.X, position.Y, index < project.GodNodeCount)
        );
    }
}
