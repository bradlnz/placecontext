using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Mark a work item finished.</summary>
public sealed record CompleteWorkItemCommand(Guid WorkItemId) : ICommand<WorkItemView>;
