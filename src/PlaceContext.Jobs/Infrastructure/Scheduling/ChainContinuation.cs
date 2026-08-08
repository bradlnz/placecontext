namespace PlaceContext.Jobs.Infrastructure.Scheduling;

internal sealed record ChainContinuation(
    Guid RunId,
    Guid TenantId,
    Guid ChainId,
    int StageIndex);
