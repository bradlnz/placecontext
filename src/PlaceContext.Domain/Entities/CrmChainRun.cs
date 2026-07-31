using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>Links a job-chain run to the customer and lifecycle stage it was launched for.</summary>
public sealed record CrmChainRun(
    Guid Id,
    Guid ProjectId,
    Guid ClientId,
    Guid ChainId,
    Guid ChainRunId,
    CustomerLifecycleStage LifecycleStage,
    DateTimeOffset StartedAt)
{
    public static CrmChainRun Create(
        Guid projectId,
        Guid clientId,
        Guid chainId,
        Guid chainRunId,
        CustomerLifecycleStage lifecycleStage,
        DateTimeOffset startedAt)
    {
        if (projectId == Guid.Empty || clientId == Guid.Empty || chainId == Guid.Empty || chainRunId == Guid.Empty)
            throw new ArgumentException("CRM chain run identifiers must not be empty.");
        return new CrmChainRun(
            Guid.NewGuid(), projectId, clientId, chainId, chainRunId, lifecycleStage, startedAt);
    }
}
