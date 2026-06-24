using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Move a work item to a board column (Queued / InProgress / Done).</summary>
public sealed record MoveWorkItemCommand(Guid WorkItemId, string Status) : ICommand<WorkItemView>;
