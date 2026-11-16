namespace PlaceContext.Application.Workload;

public interface IExecutionApprovals
{
    Task<bool> RequestAsync(Guid projectId, Guid runId, string warning, string evidence, CancellationToken ct);
    Task<IReadOnlyList<ExecutionApproval>> ListAsync(Guid projectId, IReadOnlyList<Guid> runIds, CancellationToken ct = default);
    Task DecideAsync(Guid id, bool approve, CancellationToken ct = default);
}

public sealed record ExecutionApproval(Guid Id, Guid RunId, string Warning, string Evidence,
    string Status, DateTimeOffset ExpiresAt, Guid? DecidedBy);
