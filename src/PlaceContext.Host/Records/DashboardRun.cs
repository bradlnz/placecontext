namespace PlaceContext.Host.Controllers;

public sealed record DashboardRun(
    Guid Id,
    string JobName,
    string ProjectName,
    string Status,
    int SucceededShards,
    int FailedShards,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string SourceKind);
