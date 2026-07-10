using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Replace a chain's name/description/steps.</summary>
public sealed record UpdateJobChainCommand(
    Guid ChainId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds)
    : ICommand<JobChainView>;
