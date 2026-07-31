namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat tenant-owned row for a persisted job verification case.</summary>
public sealed class JobTestCaseRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid JobId { get; set; }
    public string Name { get; set; } = "";
    public string? InputPayload { get; set; }
    public string AssertionType { get; set; } = "Succeeds";
    public string? ExpectedValue { get; set; }
    public bool Enabled { get; set; } = true;
    public string LastStatus { get; set; } = "NotRun";
    public string? LastMessage { get; set; }
    public string? LastActualOutput { get; set; }
    public Guid? LastJobRunId { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public long? LastDurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? RuntimeId { get; set; }
    public string? Entrypoint { get; set; }
    public string CodeFilesJson { get; set; } = "[]";
    public bool AllowNetworkEgress { get; set; }
    public string MethodResultsJson { get; set; } = "[]";
}
