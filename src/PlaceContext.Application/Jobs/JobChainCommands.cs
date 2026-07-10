using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Define a chain: the named, ordered list of jobs to run one after another.</summary>
public sealed record CreateJobChainCommand(
    Guid ProjectId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds)
    : ICommand<JobChainView>;

/// <summary>Replace a chain's name/description/steps.</summary>
public sealed record UpdateJobChainCommand(
    Guid ChainId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds)
    : ICommand<JobChainView>;

public sealed record DeleteJobChainCommand(Guid ChainId) : ICommand<bool>;

/// <summary>
/// Run a chain now: each step's primary output (reduce artifact if present, else the shard artifacts)
/// becomes the next step's input payload. <paramref name="InputPayload"/> feeds the FIRST step; when
/// null the first job runs with its stored shard payloads.
/// </summary>
public sealed record RunJobChainCommand(Guid ChainId, string? InputPayload = null)
    : ICommand<ChainRunView>;

public sealed record ListJobChainsQuery(Guid ProjectId) : IQuery<IReadOnlyList<JobChainView>>;
