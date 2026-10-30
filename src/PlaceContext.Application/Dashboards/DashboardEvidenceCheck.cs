namespace PlaceContext.Application.Dashboards;

public sealed record DashboardEvidenceCheck(
    string Key, string Name, DashboardCheckState State, string Reason, string NextStep);
