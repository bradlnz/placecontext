namespace PlaceContext.Application.Billing;

/// <summary>Global admission for a top-level execution. Capacity is held until the
/// job or workflow completes or pauses; a new run waits instead of being rejected.</summary>
public interface IExecutionCapacity
{
    Task<IExecutionLease> AcquireAsync(Guid executionId, string kind, CancellationToken ct = default);
}
