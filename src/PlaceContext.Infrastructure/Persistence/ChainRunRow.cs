namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.ChainRun"/> (table chain_runs).
/// Steps are serialized as a JSON array; names are snapshots so history survives renames/deletes.</summary>
public sealed class ChainRunRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ChainId { get; set; }
    public Guid ProjectId { get; set; }
    public string ChainName { get; set; } = "";
    public string Status { get; set; } = "";
    public string StepsJson { get; set; } = "[]";
    public string? FinalOutput { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? ResumeAt { get; set; }
    public int? ResumeStageIndex { get; set; }
    public Guid? CrmClientId { get; set; }
    public string? ContinuationClaimedBy { get; set; }
    public DateTimeOffset? ContinuationClaimedAt { get; set; }
    public string? ContinuationOverrides { get; set; }
}
