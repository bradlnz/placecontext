using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>Links a job run to the customer and lifecycle stage it was launched for.</summary>
public sealed record CrmJobRun(
    Guid Id,
    Guid ProjectId,
    Guid ClientId,
    Guid JobId,
    Guid RunId,
    CustomerLifecycleStage LifecycleStage,
    DateTimeOffset StartedAt)
{
    public static CrmJobRun Create(
        Guid projectId,
        Guid clientId,
        Guid jobId,
        Guid runId,
        CustomerLifecycleStage lifecycleStage,
        DateTimeOffset startedAt)
    {
        if (projectId == Guid.Empty || clientId == Guid.Empty || jobId == Guid.Empty || runId == Guid.Empty)
            throw new ArgumentException("CRM job run identifiers must not be empty.");
        return new CrmJobRun(Guid.NewGuid(), projectId, clientId, jobId, runId, lifecycleStage, startedAt);
    }
}
