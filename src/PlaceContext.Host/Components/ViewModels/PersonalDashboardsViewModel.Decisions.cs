using System.Globalization;
using PlaceContext.Application.Dashboards;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class PersonalDashboardsViewModel
{
    public List<DashboardWatchTarget> Targets { get; } = [];
    public IReadOnlyList<DashboardSignal> Signals { get; private set; } = [];
    public string DecisionFingerprint { get; private set; } = "";
    public bool TargetsDirty { get; private set; }
    public bool EvaluatingDecisions { get; private set; }
    public DateTimeOffset? DecisionsEvaluatedAt { get; private set; }
    public bool TargetEditorOpen { get; private set; }
    public string TargetSeries { get; set; } = "";
    public DashboardMeasureStatistic TargetStatistic { get; set; } = DashboardMeasureStatistic.Maximum;
    public DashboardTargetDirection TargetDirection { get; set; } = DashboardTargetDirection.AtMost;
    public string TargetThreshold { get; set; } = "";
    public string? TargetError { get; private set; }
    private Guid? _targetChartId;
    private readonly Dictionary<string, DashboardDecision> _decisions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _decisionStop;

    public DashboardSignal? FocusedSignal => Signals.FirstOrDefault(x => x.ChartId == FocusedChartId);
    public IReadOnlyList<DashboardIndicatorProfile> IndicatorProfiles => DashboardIndicatorProfiles.Create(
        new DashboardSnapshot(SelectedCharts.Where(chart =>
            VisibleProfiles.Any(profile => profile.ChartId == chart.Id)).ToArray()),
        AssessedAt, Targets);
    public int MeasuredIndicatorCount => IndicatorProfiles.Sum(profile =>
        profile.Indicators.Count(indicator => indicator.Measurement is not null));
    public int IndicatorValueCount => IndicatorProfiles.Sum(profile =>
        profile.Indicators.Sum(indicator => indicator.Measurement?.Count ?? 0));
    public int MeasuredSignalCount => Signals.Count(x => x.Measurement is not null);
    public int MeasuredValueCount => Signals.Sum(x => x.Measurement?.Count ?? 0);
    public int ReadySignalCount => Signals.Count(x => !RequiresData(x));
    public int ReviewDecisionCount => Signals.Count(x => DecisionFor(x.Id)?.Status == DashboardDecisionStatus.Review);
    public int EvaluatedDecisionCount => Signals.Count(x => DecisionFor(x.Id)?.Probability is not null);
    public bool CanEvaluateDecisions => !Busy && !Editing && !MissingCharts && ReadySignalCount > 0;
    public IReadOnlyList<DashboardSignal> VisibleSignals => VisibleProfiles
        .Select(profile => Signals.FirstOrDefault(x => x.ChartId == profile.ChartId))
        .OfType<DashboardSignal>().OrderBy(DecisionPriority).ThenBy(x => x.ChartName).ToArray();

    public DashboardDecision? DecisionFor(string id)
    {
        if (MissingCharts || !_decisions.TryGetValue(id, out var decision) || decision.Fingerprint != DecisionFingerprint) return null;
        if (Signals.FirstOrDefault(x => x.Id == id) is not { } signal) return null;
        return signal.NeedsData && decision.Status != DashboardDecisionStatus.NeedsData ? null : decision;
    }

    public bool RequiresData(DashboardSignal signal) => signal.NeedsData
        || DecisionFor(signal.Id)?.Status == DashboardDecisionStatus.NeedsData;

    public string DecisionLabel(DashboardSignal signal) => RequiresData(signal) ? "Needs data"
        : DecisionFor(signal.Id)?.Status switch
        {
            DashboardDecisionStatus.Review => "Review",
            DashboardDecisionStatus.Watch => "Watch",
            DashboardDecisionStatus.Hold => "Hold",
            DashboardDecisionStatus.Unavailable => "Unavailable",
            _ => "Ready for assessment"
        };
    public string DecisionTone(DashboardSignal signal) => RequiresData(signal) ? "warning"
        : DecisionFor(signal.Id)?.Status switch
        {
            DashboardDecisionStatus.Review => "primary",
            DashboardDecisionStatus.Watch => "success",
            DashboardDecisionStatus.Hold => "warning",
            DashboardDecisionStatus.Unavailable => "error",
            _ => "neutral"
        };
    public string DecisionIcon(DashboardSignal signal) => RequiresData(signal) ? "refresh"
        : DecisionFor(signal.Id)?.Status switch
        {
            DashboardDecisionStatus.Review => "decision",
            DashboardDecisionStatus.Watch => "check-circle",
            DashboardDecisionStatus.Hold => "pause",
            DashboardDecisionStatus.Unavailable => "warning",
            _ => "measure"
        };
    private int DecisionPriority(DashboardSignal signal) => DecisionFor(signal.Id)?.Status switch
    {
        DashboardDecisionStatus.Review => 0,
        DashboardDecisionStatus.Hold => 2,
        DashboardDecisionStatus.Unavailable => 3,
        DashboardDecisionStatus.Watch => 5,
        _ => RequiresData(signal) ? 1 : 4
    };
    public static string FormatMeasure(double value) => value.ToString(
        Math.Abs(value) >= 1_000_000_000 || value != 0 && Math.Abs(value) < .01 ? "G6" : "N2", CultureInfo.CurrentCulture);
    public static string FormatTargetValue(double value) => value.ToString("R", CultureInfo.CurrentCulture);
    public static string TargetLabel(DashboardWatchTarget target)
        => $"{target.Statistic} {(target.Direction == DashboardTargetDirection.AtMost ? "at most" : "at least")} {FormatTargetValue(target.Threshold)}";

    public void ShowDecisions()
    {
        SetLens(DashboardLens.Decisions);
        if (VisibleSignals.FirstOrDefault() is { } first) Focus(first.ChartId);
    }
    public void EditTarget()
    {
        if (Busy || FocusedSignal is not { } signal) return;
        var target = Targets.FirstOrDefault(x => x.ChartId == signal.ChartId);
        _targetChartId = signal.ChartId;
        TargetSeries = target?.SeriesName ?? signal.SeriesName;
        TargetStatistic = target?.Statistic ?? DashboardMeasureStatistic.Maximum;
        TargetDirection = target?.Direction ?? DashboardTargetDirection.AtMost;
        TargetThreshold = target?.Threshold.ToString("G", CultureInfo.InvariantCulture) ?? "";
        TargetError = null; TargetEditorOpen = true;
    }
    public void CancelTarget() { if (!Busy) { TargetEditorOpen = false; TargetError = null; } }
    public void ApplyTarget()
    {
        if (Busy || FocusedChart is not { } chart || _targetChartId != chart.Id) return;
        if (!double.TryParse(TargetThreshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
            || !double.IsFinite(threshold))
        { TargetError = "Enter a finite number in the chart's units."; return; }
        if (!Enum.IsDefined(TargetStatistic) || !Enum.IsDefined(TargetDirection)
            || chart.Spec.Series.Count(x => x.Name == TargetSeries) != 1 || string.IsNullOrWhiteSpace(TargetSeries))
        { TargetError = "Choose a distinct named measure and a valid comparison."; return; }
        Targets.RemoveAll(x => x.ChartId == chart.Id);
        Targets.Add(new(chart.Id, TargetSeries, TargetStatistic, TargetDirection, threshold));
        TargetsDirty = true; TargetEditorOpen = false; TargetError = null;
        RebuildProfile();
        Notice = "Target applied. Save this view to keep the target; Decisions must be evaluated again.";
    }
    public void RemoveTarget()
    {
        if (Busy || FocusedSignal is not { } signal || Targets.RemoveAll(x => x.ChartId == signal.ChartId) == 0) return;
        TargetsDirty = true; TargetEditorOpen = false; TargetError = null;
        RebuildProfile();
        Notice = "Target removed. This signal now uses distribution review. Save the view to keep this change.";
    }

    public Task EvaluateDecisionsAsync() => !CanEvaluateDecisions ? Task.CompletedTask : EvaluateSignalsAsync(Signals.Select(x => x.Id).ToArray());
    public Task EvaluateSignalAsync(string id) => !CanEvaluateDecisions || !Signals.Any(x => x.Id == id && !RequiresData(x))
        ? Task.CompletedTask : EvaluateSignalsAsync([id]);
    public void CancelDecisionReview() => _decisionStop?.Cancel();

    private Task EvaluateSignalsAsync(IReadOnlyList<string> ids) => ExecuteAsync(async () =>
    {
        RebuildProfile();
        var expected = DecisionFingerprint;
        foreach (var id in ids) _decisions.Remove(id);
        EvaluatingDecisions = true;
        using var reviewStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _decisionStop = reviewStop;
        NotifyStateChanged();
        try
        {
            var result = await dashboards.DecideAsync(ProjectId, ChartIds.ToArray(), expected, ids, Targets.ToArray(), reviewStop.Token);
            reviewStop.Token.ThrowIfCancellationRequested();
            if (result.Fingerprint != expected || DecisionFingerprint != expected
                || result.Decisions.Count != ids.Count
                || result.Decisions.Any(x => x.Fingerprint != expected || !ids.Contains(x.SignalId))
                || result.Decisions.Select(x => x.SignalId).Distinct().Count() != result.Decisions.Count)
                throw new InvalidOperationException("Decision evidence changed. Reload snapshots and evaluate again.");
            foreach (var decision in result.Decisions) _decisions[decision.SignalId] = decision;
            DecisionsEvaluatedAt = result.EvaluatedAt;
            Lens = DashboardLens.Decisions; _rendered.Clear();
            EnsureFocus();
            if (ids.Count == 1 && Signals.FirstOrDefault(x => x.Id == ids[0]) is { } chosen) FocusedChartId = chosen.ChartId;
            else if (VisibleSignals.FirstOrDefault() is { } first) FocusedChartId = first.ChartId;
            TargetEditorOpen = false;
            var judgments = result.Decisions.Count(x => x.Probability is not null);
            var needsData = result.Decisions.Count(x => x.Status == DashboardDecisionStatus.NeedsData);
            var unavailable = result.Decisions.Count(x => x.Status == DashboardDecisionStatus.Unavailable);
            Notice = $"AI review assessed {judgments} signal(s)."
                + (needsData > 0 ? $" {needsData} need refreshed evidence." : "")
                + (unavailable > 0 ? $" {unavailable} unavailable; retry those reviews." : " Decisions apply to these saved snapshots.");
            DecisionSummaryNotice = judgments > 0 && unavailable == 0;
        }
        catch (OperationCanceledException) when (reviewStop.IsCancellationRequested)
        { Notice = "Decision review canceled. No new results were applied."; }
        finally
        {
            EvaluatingDecisions = false;
            _decisionStop = null;
        }
    });

    private void RebuildSignals()
    {
        var fingerprint = DashboardSignals.Fingerprint(Snapshot, Targets);
        if (fingerprint != DecisionFingerprint) InvalidateDecisions();
        DecisionFingerprint = fingerprint;
        var available = SelectedCharts.Select(x => x.Id).ToHashSet();
        Signals = DashboardSignals.Create(Snapshot, AssessedAt, Targets.Where(x => available.Contains(x.ChartId)).ToArray());
    }
    private void InvalidateDecisions() { _decisions.Clear(); DecisionsEvaluatedAt = null; }
    private void ResetDecisionWorkspace(IReadOnlyList<DashboardWatchTarget>? targets = null)
    {
        Targets.Clear();
        if (targets is not null) Targets.AddRange(targets);
        TargetsDirty = false; TargetEditorOpen = false; TargetError = null; _targetChartId = null;
        InvalidateDecisions(); Signals = []; DecisionFingerprint = "";
    }
}
