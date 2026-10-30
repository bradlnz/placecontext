namespace PlaceContext.Application.Dashboards;

public sealed record DashboardDecisionSet(string Fingerprint, DateTimeOffset EvaluatedAt,
    IReadOnlyList<DashboardDecision> Decisions, int ModelCalls = 0);
