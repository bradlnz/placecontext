using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Claim the next queued work item for a project (highest priority, then oldest); null if empty.</summary>
public sealed record NextWorkItemCommand(Guid ProjectId) : ICommand<WorkItemView?>;
