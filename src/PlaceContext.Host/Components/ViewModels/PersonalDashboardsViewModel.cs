using Microsoft.JSInterop;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public enum DashboardLens { Overview, Actions, Decisions, NeedsAttention, Explore }

public sealed partial class PersonalDashboardsViewModel(IPersonalDashboards dashboards, PortalUiState ui, IJSRuntime js, IClock? clock = null, IWorkflowActionInbox? actionInbox = null)
    : PageViewModel, IComponentViewModel, IDisposable
{
    public Guid ProjectId { get; private set; }
    public IReadOnlyList<PersonalDashboard> Saved { get; private set; } = [];
    public IReadOnlyList<DashboardChart> Charts { get; private set; } = [];
    public List<Guid> ChartIds { get; } = [];
    public string Name { get; set; } = "My dashboard";
    private string _question = "";
    public string Question
    {
        get => _question;
        set { if (_question == value) return; _question = value ?? ""; Analysis = null; }
    }
    public PersonalDashboard? Selected { get; private set; }
    public DashboardAnalysis? Analysis { get; private set; }
    public bool Editing { get; private set; }
    public bool Busy { get; private set; }
    public bool ConfirmDelete { get; set; }
    public string? Error { get; private set; }
    private string? _notice;
    public string? Notice
    {
        get => _notice;
        private set { _notice = value; DecisionSummaryNotice = false; }
    }
    public bool DecisionSummaryNotice { get; private set; }
    public DashboardLens Lens { get; private set; }
    public Guid? FocusedChartId { get; private set; }
    public DateTimeOffset AssessedAt { get; private set; }
    public DashboardSnapshotProfile Profile { get; private set; } = DashboardSnapshotProfile.Create(new([]), DateTimeOffset.UtcNow);
    public bool IsPreview => Selected is null && !Editing && ChartIds.Count > 0;
    private string _profileSearch = "";
    public string ProfileSearch
    {
        get => _profileSearch;
        set { if (_profileSearch == value) return; _profileSearch = value; EnsureFocus(); Analysis = null; _rendered.Clear(); }
    }
    public IEnumerable<DashboardChart> SelectedCharts => ChartIds.Select(id => Charts.FirstOrDefault(x => x.Id == id)).OfType<DashboardChart>();
    public IReadOnlyList<Guid> MissingChartIds => ChartIds.Except(Charts.Select(x => x.Id)).ToArray();
    public bool MissingCharts => MissingChartIds.Count > 0;
    public DashboardSnapshot Snapshot => new(SelectedCharts.ToArray());
    public bool AnalysisCurrent => !MissingCharts && Analysis is not null && Analysis.Fingerprint == Snapshot.Fingerprint && Analysis.Question == Question.Trim();
    public bool CanAnalyze => !Busy && !MissingCharts && SelectedCharts.Any() && !string.IsNullOrWhiteSpace(Question) && Question.Length <= 1000;
    public IReadOnlyList<DashboardDataProfile> VisibleProfiles
    {
        get
        {
            IEnumerable<DashboardDataProfile> profiles = Profile.Charts;
            if (Lens == DashboardLens.NeedsAttention) profiles = profiles.Where(x => x.NeedsAttention);
            if (!string.IsNullOrWhiteSpace(ProfileSearch))
                profiles = profiles.Where(x => x.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase)
                    || (x.Source?.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase) ?? false)
                    || x.Series.Any(s => s.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase)));
            if (Lens != DashboardLens.Explore) profiles = profiles.OrderByDescending(x => x.NeedsAttention).ThenBy(x => x.GeneratedAt);
            return profiles.ToArray();
        }
    }
    public DashboardDataProfile? FocusedProfile => VisibleProfiles.FirstOrDefault(x => x.ChartId == FocusedChartId);
    public DashboardChart? FocusedChart => SelectedCharts.FirstOrDefault(x => x.Id == FocusedChartId);
    public IReadOnlyList<DashboardDataProfile> DisplayedProfiles => Lens == DashboardLens.Decisions ? []
        : Lens == DashboardLens.Explore ? FocusedProfile is { } focused ? [focused] : [] : VisibleProfiles;
    public IReadOnlyList<DashboardChart> DisplayedCharts => DisplayedProfiles
        .Select(profile => SelectedCharts.FirstOrDefault(chart => chart.Id == profile.ChartId))
        .OfType<DashboardChart>().ToArray();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<Guid, string> _rendered = [];
    private Task _operation = Task.CompletedTask;
    private bool _disposed;

    public async Task LoadAsync(Guid projectId)
    {
        await _operation;
        if (_disposed) return;
        ProjectId = projectId;
        Selected = null; Analysis = null; ChartIds.Clear(); Charts = []; Saved = []; Editing = false; _rendered.Clear();
        ResetDecisionWorkspace();
        WorkflowActions = []; SelectedWorkflowActionId = null; ActionError = ActionNotice = null;
        Lens = DashboardLens.Overview; _profileSearch = ""; FocusedChartId = null; Question = ""; ConfirmDelete = false;
        RebuildProfile();
        ui.Set("Data profiles", "Measured signals and decisions with Jev");
        await ExecuteAsync(async () =>
        {
            Saved = await dashboards.ListAsync(ProjectId, _stop.Token);
            Charts = await dashboards.ChartsAsync(ProjectId, _stop.Token);
            await LoadWorkflowActionsCoreAsync();
            if (Saved.FirstOrDefault() is { } first) OpenCore(first);
            else PreviewCore();
        });
    }

    public void New()
    {
        if (Busy) return;
        Selected = null; Name = "My dashboard"; Question = ""; ChartIds.Clear();
        ResetDecisionWorkspace();
        Analysis = null; Error = Notice = null; ConfirmDelete = false; Editing = true; _rendered.Clear();
        Lens = DashboardLens.Explore; _profileSearch = ""; RebuildProfile();
    }
    public void Open(PersonalDashboard dashboard) { if (!Busy) OpenCore(dashboard); }
    public void OpenSaved(string? value)
    {
        if (Busy) return;
        if (Guid.TryParse(value, out var id) && Saved.FirstOrDefault(x => x.Id == id) is { } dashboard) OpenCore(dashboard);
        else PreviewCore();
    }
    public void PreviewAvailable() { if (!Busy) PreviewCore(); }
    private void PreviewCore()
    {
        Selected = null; Name = "Project overview"; Question = ""; ChartIds.Clear();
        ResetDecisionWorkspace();
        Error = Notice = null;
        ChartIds.AddRange(Charts.Take(12).Select(x => x.Id));
        Editing = false; ConfirmDelete = false; Analysis = null; _profileSearch = "";
        Lens = DashboardLens.Overview; _rendered.Clear(); RebuildProfile();
    }
    private void OpenCore(PersonalDashboard dashboard)
    {
        Selected = dashboard; Name = dashboard.Name; Question = dashboard.Question;
        ResetDecisionWorkspace(dashboard.Targets);
        ChartIds.Clear(); ChartIds.AddRange(dashboard.ChartIds); Analysis = null;
        Editing = false; ConfirmDelete = false; Error = Notice = null; _rendered.Clear();
        _profileSearch = ""; Lens = DashboardLens.Overview; RebuildProfile();
    }
    public void Edit() { if (!Busy) { Editing = true; ConfirmDelete = false; } }
    public void Cancel() { if (Busy) return; if (Selected is not null) OpenCore(Selected); else PreviewCore(); }
    public void SetLens(DashboardLens lens)
    {
        if (Busy || Lens == lens) return;
        Lens = lens; EnsureFocus(); Analysis = null; _rendered.Clear();
    }
    public void Focus(Guid chartId)
    {
        if (Busy || !VisibleProfiles.Any(x => x.ChartId == chartId) || FocusedChartId == chartId) return;
        FocusedChartId = chartId; Analysis = null;
        TargetEditorOpen = false;
        if (Lens == DashboardLens.Explore) _rendered.Clear();
    }
    public void UseSuggestion(string question)
    {
        if (Busy || FocusedProfile is not { } profile || !profile.SuggestedQuestions.Contains(question)) return;
        Question = question; Analysis = null; Notice = "Question prepared. Send it when you are ready.";
    }
    public void RemoveMissing()
    {
        if (Busy) return;
        var missing = MissingChartIds.ToHashSet();
        ChartIds.RemoveAll(missing.Contains);
        Targets.RemoveAll(target => missing.Contains(target.ChartId));
        Analysis = null; _rendered.Clear(); RebuildProfile();
    }
    public void Toggle(Guid id)
    {
        if (Busy || !Charts.Any(x => x.Id == id) && !ChartIds.Contains(id)) return;
        if (!ChartIds.Remove(id) && ChartIds.Count < 12) ChartIds.Add(id);
        Targets.RemoveAll(target => !ChartIds.Contains(target.ChartId));
        Analysis = null; _rendered.Clear(); RebuildProfile();
    }
    public void Move(Guid id, int direction)
    {
        if (Busy) return;
        var index = ChartIds.IndexOf(id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ChartIds.Count) return;
        (ChartIds[index], ChartIds[target]) = (ChartIds[target], ChartIds[index]);
        Analysis = null; _rendered.Clear(); RebuildProfile();
    }
    public Task SaveAsync() => ExecuteAsync(async () =>
    {
        var saved = await dashboards.SaveAsync(ProjectId, new(Selected?.Id ?? Guid.Empty, Name,
            ChartIds.ToArray(), Question, Selected?.Version ?? Guid.Empty, Targets.ToArray()), _stop.Token);
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
        else PreviewCore();
        Notice = "Dashboard deleted. Your source charts are still available.";
    });
    public Task RefreshAsync() => ExecuteAsync(async () =>
    {
        Charts = await dashboards.ChartsAsync(ProjectId, _stop.Token);
        await LoadWorkflowActionsCoreAsync();
        if (Selected is null && !Editing)
        {
            ChartIds.Clear();
            ChartIds.AddRange(Charts.Take(12).Select(x => x.Id));
            if (Targets.RemoveAll(target => !ChartIds.Contains(target.ChartId)) > 0) TargetsDirty = true;
        }
        InvalidateDecisions();
        Analysis = null; _rendered.Clear(); RebuildProfile(); Notice = "Loaded the latest saved snapshots. Source queries have not been rerun. Re-evaluate decisions against this evidence.";
    });
    public Task AnalyzeAsync() => !CanAnalyze ? Task.CompletedTask : ExecuteAsync(async () =>
    {
        Analysis = null;
        Analysis = await dashboards.AnalyzeAsync(ProjectId, ChartIds.ToArray(), Snapshot.Fingerprint, Question, _stop.Token);
    });

    public async Task RenderAsync()
    {
        var charts = DisplayedCharts.Where(chart => !_rendered.TryGetValue(chart.Id, out var spec)
            || spec != chart.Spec.ToJson()).ToArray();
        if (charts.Length == 0) return;
        try
        {
            var entries = charts.Select(x => new { id = CanvasId(x.Id), spec = x.Spec.ToJson() }).ToArray();
            await js.InvokeVoidAsync("pcchart.renderAll", new object[] { entries });
            foreach (var chart in charts) _rendered[chart.Id] = chart.Spec.ToJson();
        }
        catch (JSException) { }
    }
    public static string CanvasId(Guid id) => $"personal-chart-{id:N}";
    public static string BandClass(DashboardConfidence confidence) => confidence.ToString().ToLowerInvariant();
    public static string CheckClass(DashboardCheckState state) => state.ToString().ToLowerInvariant();
    public static string ValueAt(ChartSeries? series, int index) => series?.Values is { } values && index >= 0 && index < values.Count && double.IsFinite(values[index])
        ? values[index].ToString("N2") : "Not available";
    private void RebuildProfile()
    {
        AssessedAt = clock?.UtcNow ?? DateTimeOffset.UtcNow;
        Profile = DashboardSnapshotProfile.Create(Snapshot, AssessedAt);
        if (MissingCharts) Profile = Profile with { Confidence = DashboardConfidence.Unknown,
            ConfidenceReason = $"{MissingChartIds.Count} selected chart(s) are unavailable. Restore them or update this view before relying on a combined assessment.",
            NeedsAttentionCount = Profile.NeedsAttentionCount + MissingChartIds.Count };
        RebuildSignals();
        EnsureFocus();
    }
    private void EnsureFocus()
    {
        if (!VisibleProfiles.Any(x => x.ChartId == FocusedChartId))
        {
            FocusedChartId = VisibleProfiles.FirstOrDefault()?.ChartId;
            TargetEditorOpen = false;
        }
    }
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
