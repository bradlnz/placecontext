using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Tests;

internal sealed class DashboardUiStub : IPersonalDashboards
{
    public List<PersonalDashboard> Saved { get; } = [];
    public int Analyses { get; private set; }
    public bool FailRead { get; set; }
    public int DecisionCalls { get; private set; }
    public bool WrongDecisionFingerprint { get; set; }
    public bool DelayDecisions { get; set; }
    public bool IgnoreDecisionCancellation { get; set; }
    public bool ServerRequiresData { get; set; }
    public TaskCompletionSource ReleaseDecision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DecisionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<string> LastSignalIds { get; private set; } = [];
    public IReadOnlyList<DashboardChart> Charts { get; set; } = Enumerable.Range(1, 2).Select(i => new DashboardChart(
        Guid.NewGuid(), $"Chart {i}", new ChartSpec("bar", $"Chart {i}", ["A"], [new ChartSeries("Value", [i])]), DateTimeOffset.UtcNow)).ToArray();
    public Task<IReadOnlyList<PersonalDashboard>> ListAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersonalDashboard>>(Saved.ToArray());
    public Task<IReadOnlyList<DashboardChart>> ChartsAsync(Guid projectId, CancellationToken ct = default)
        => FailRead ? throw new InvalidOperationException("Charts unavailable") : Task.FromResult(Charts);
    public Task<PersonalDashboard> SaveAsync(Guid projectId, PersonalDashboard dashboard, CancellationToken ct = default)
    {
        var saved = dashboard with { Id = dashboard.Id == Guid.Empty ? Guid.NewGuid() : dashboard.Id, Version = Guid.NewGuid() };
        Saved.RemoveAll(x => x.Id == saved.Id); Saved.Add(saved); return Task.FromResult(saved);
    }
    public Task DeleteAsync(Guid projectId, Guid id, Guid version, CancellationToken ct = default) { Saved.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public Task<DashboardAnalysis> AnalyzeAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint, string question, CancellationToken ct = default)
    {
        Analyses++;
        return Task.FromResult(new DashboardAnalysis(question, .95, DateTimeOffset.UtcNow, fingerprint));
    }
    public async Task<DashboardDecisionSet> DecideAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint,
        IReadOnlyList<string> signalIds, IReadOnlyList<DashboardWatchTarget>? targets, CancellationToken ct = default)
    {
        DecisionCalls++; LastSignalIds = signalIds.ToArray(); DecisionStarted.TrySetResult();
        if (DelayDecisions)
        {
            if (IgnoreDecisionCancellation) await ReleaseDecision.Task;
            else await Task.Delay(Timeout.Infinite, ct);
        }
        var snapshot = new DashboardSnapshot(chartIds.Select(id => Charts.Single(x => x.Id == id)).ToArray());
        var actual = WrongDecisionFingerprint ? "changed-evidence" : DashboardSignals.Fingerprint(snapshot, targets);
        var now = DateTimeOffset.UtcNow;
        var signals = DashboardSignals.Create(snapshot, now, targets).Where(x => signalIds.Contains(x.Id)).ToArray();
        var decisions = signals.Select(signal => ServerRequiresData
            ? DashboardDecision.ForMissingData(signal, now, actual)
            : DashboardDecision.FromJudgment(signal, .95, "fixture-jev", now, actual)).ToArray();
        return new(actual, now, decisions, signals.Count(x => !x.NeedsData));
    }
}
