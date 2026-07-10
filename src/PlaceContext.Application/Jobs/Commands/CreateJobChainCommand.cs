using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Define a chain: the named, ordered list of jobs to run one after another.</summary>
public sealed record CreateJobChainCommand(
    Guid ProjectId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds)
    : ICommand<JobChainView>;
