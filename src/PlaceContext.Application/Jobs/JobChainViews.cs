namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a job chain definition.</summary>
public sealed record JobChainView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<JobChainStepView> Steps,
    DateTimeOffset UpdatedAt);

/// <summary>One step of a chain: the job it runs. JobName is "(deleted)" when the job no longer exists.</summary>
public sealed record JobChainStepView(Guid JobId, string JobName);

/// <summary>
/// The result of running a chain: one entry per executed step (steps after a failure never run),
/// plus the final payload — the last step's primary output, i.e. what the chain produced.
/// </summary>
public sealed record ChainRunView(
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<ChainStepRunView> Steps,
    string? FinalOutput);

/// <summary>One executed chain step and the job run it produced.</summary>
public sealed record ChainStepRunView(int Index, Guid JobId, string JobName, Guid RunId, string Status);
