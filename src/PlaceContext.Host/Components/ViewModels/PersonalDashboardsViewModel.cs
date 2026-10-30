using Microsoft.JSInterop;
using PlaceContext.Application.Dashboards;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class PersonalDashboardsViewModel(IPersonalDashboards dashboards, PortalUiState ui, IJSRuntime js)
    : PageViewModel, IComponentViewModel, IDisposable
{
    public Guid ProjectId { get; private set; }
    public IReadOnlyList<PersonalDashboard> Saved { get; private set; } = [];
    public IReadOnlyList<DashboardChart> Charts { get; private set; } = [];
    public List<Guid> ChartIds { get; } = [];
    public string Name { get; set; } = "My dashboard";
    public string Question { get; set; } = "";
    public PersonalDashboard? Selected { get; private set; }
    public DashboardAnalysis? Analysis { get; private set; }
    public bool Editing { get; private set; }
    public bool Busy { get; private set; }
    public bool ConfirmDelete { get; set; }
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public IEnumerable<DashboardChart> SelectedCharts => ChartIds.Select(id => Charts.FirstOrDefault(x => x.Id == id)).OfType<DashboardChart>();
    public bool MissingCharts => SelectedCharts.Count() != ChartIds.Count;
    public DashboardSnapshot Snapshot => new(SelectedCharts.ToArray());
    public bool AnalysisCurrent => Analysis is not null && Analysis.Fingerprint == Snapshot.Fingerprint && Analysis.Question == Question.Trim();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<Guid, string> _rendered = [];
    private Task _operation = Task.CompletedTask;
    private bool _disposed;

    public async Task LoadAsync(Guid projectId)
    {
        await _operation;
        if (_disposed) return;
        ProjectId = projectId;
        Selected = null; Analysis = null; ChartIds.Clear(); Editing = false; _rendered.Clear();
        ui.Set("My dashboards", "Your data, your perspective");
        await ExecuteAsync(async () =>
        {
            Saved = await dashboards.ListAsync(ProjectId, _stop.Token);
            Charts = await dashboards.ChartsAsync(ProjectId, _stop.Token);
            if (Saved.FirstOrDefault() is { } first) OpenCore(first);
        });
    }

    public void New()
    {
        if (Busy) return;
        Selected = null; Name = "My dashboard"; Question = ""; ChartIds.Clear();
        Analysis = null; Error = Notice = null; ConfirmDelete = false; Editing = true; _rendered.Clear();
    }
    public void Open(PersonalDashboard dashboard) { if (!Busy) OpenCore(dashboard); }
    private void OpenCore(PersonalDashboard dashboard)
    {
        Selected = dashboard; Name = dashboard.Name; Question = dashboard.Question;
        ChartIds.Clear(); ChartIds.AddRange(dashboard.ChartIds); Analysis = null;
        Editing = false; ConfirmDelete = false; Error = Notice = null; _rendered.Clear();
    }
    public void Edit() { if (!Busy) { Editing = true; ConfirmDelete = false; } }
    public void Cancel() { if (Busy) return; if (Selected is not null) OpenCore(Selected); else Editing = false; }
    public void Toggle(Guid id)
    {
        if (Busy) return;
        if (!ChartIds.Remove(id) && ChartIds.Count < 12) ChartIds.Add(id);
        Analysis = null; _rendered.Clear();
    }
    public void Move(Guid id, int direction)
    {
        if (Busy) return;
        var index = ChartIds.IndexOf(id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ChartIds.Count) return;
        (ChartIds[index], ChartIds[target]) = (ChartIds[target], ChartIds[index]);
        Analysis = null; _rendered.Clear();
    }
    public Task SaveAsync() => ExecuteAsync(async () =>
    {
        var saved = await dashboards.SaveAsync(ProjectId, new(Selected?.Id ?? Guid.Empty, Name,
            ChartIds.ToArray(), Question, Selected?.Version ?? Guid.Empty), _stop.Token);
        Saved = await dashboards.ListAsync(ProjectId, _stop.Token);
        OpenCore(saved); Notice = "Dashboard saved. Only you can see this layout.";
    });
    public Task DeleteAsync() => ExecuteAsync(async () =>
    {
        if (Selected is null || !ConfirmDelete) return;
        await dashboards.DeleteAsync(ProjectId, Selected.Id, Selected.Version, _stop.Token);
        Saved = await dashboards.ListAsync(ProjectId, _stop.Token);
        Selected = null; ChartIds.Clear(); Analysis = null; Editing = false; ConfirmDelete = false;
        if (Saved.FirstOrDefault() is { } first) OpenCore(first);
        Notice = "Dashboard deleted. Your source charts are still available.";
    });
    public Task RefreshAsync() => ExecuteAsync(async () =>
    {
        Charts = await dashboards.ChartsAsync(ProjectId, _stop.Token);
        Analysis = null; _rendered.Clear(); Notice = "Loaded the latest saved chart snapshots.";
    });
    public Task AnalyzeAsync() => ExecuteAsync(async () =>
    {
        Analysis = null;
        Analysis = await dashboards.AnalyzeAsync(ProjectId, ChartIds.ToArray(), Snapshot.Fingerprint, Question, _stop.Token);
    });

    public async Task RenderAsync()
    {
        var charts = SelectedCharts.Where(x => !_rendered.TryGetValue(x.Id, out var spec) || spec != x.Spec.ToJson()).ToArray();
        if (charts.Length == 0) return;
        try
        {
            await js.InvokeVoidAsync("pcchart.renderAll", charts.Select(x => new { id = CanvasId(x.Id), spec = x.Spec.ToJson() }).ToArray());
            foreach (var chart in charts) _rendered[chart.Id] = chart.Spec.ToJson();
        }
        catch (JSException) { }
    }
    public static string CanvasId(Guid id) => $"personal-chart-{id:N}";
    private Task ExecuteAsync(Func<Task> action) => Busy || _disposed ? Task.CompletedTask : _operation = ExecuteCoreAsync(action);
    private async Task ExecuteCoreAsync(Func<Task> action)
    {
        Busy = true; Error = Notice = null; NotifyStateChanged();
        try { await action(); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; NotifyStateChanged(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _stop.Cancel(); _stop.Dispose(); Detach();
    }
}
