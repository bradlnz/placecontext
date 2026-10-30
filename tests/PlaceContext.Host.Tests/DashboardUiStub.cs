using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Tests;

internal sealed class DashboardUiStub : IPersonalDashboards
{
    public List<PersonalDashboard> Saved { get; } = [];
    public IReadOnlyList<DashboardChart> Charts { get; } = Enumerable.Range(1, 2).Select(i => new DashboardChart(
        Guid.NewGuid(), $"Chart {i}", new ChartSpec("bar", $"Chart {i}", ["A"], [new ChartSeries("Value", [i])]), DateTimeOffset.UtcNow)).ToArray();
    public Task<IReadOnlyList<PersonalDashboard>> ListAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersonalDashboard>>(Saved.ToArray());
    public Task<IReadOnlyList<DashboardChart>> ChartsAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(Charts);
    public Task<PersonalDashboard> SaveAsync(Guid projectId, PersonalDashboard dashboard, CancellationToken ct = default)
    {
        var saved = dashboard with { Id = dashboard.Id == Guid.Empty ? Guid.NewGuid() : dashboard.Id, Version = Guid.NewGuid() };
        Saved.RemoveAll(x => x.Id == saved.Id); Saved.Add(saved); return Task.FromResult(saved);
    }
    public Task DeleteAsync(Guid projectId, Guid id, Guid version, CancellationToken ct = default) { Saved.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public Task<DashboardAnalysis> AnalyzeAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint, string question, CancellationToken ct = default)
        => Task.FromResult(new DashboardAnalysis(question, .95, DateTimeOffset.UtcNow, fingerprint));
}
