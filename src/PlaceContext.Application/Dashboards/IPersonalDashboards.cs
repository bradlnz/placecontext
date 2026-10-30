namespace PlaceContext.Application.Dashboards;

public interface IPersonalDashboards
{
    Task<IReadOnlyList<PersonalDashboard>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<DashboardChart>> ChartsAsync(Guid projectId, CancellationToken ct = default);
    Task<PersonalDashboard> SaveAsync(Guid projectId, PersonalDashboard dashboard, CancellationToken ct = default);
    Task DeleteAsync(Guid projectId, Guid id, Guid version, CancellationToken ct = default);
    Task<DashboardAnalysis> AnalyzeAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint, string question, CancellationToken ct = default);
    Task<DashboardDecisionSet> DecideAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint,
        IReadOnlyList<string> signalIds, IReadOnlyList<DashboardWatchTarget>? targets, CancellationToken ct = default);
}
