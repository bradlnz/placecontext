using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Queue a work item (a change to do) against a project.</summary>
public sealed record AddWorkItemCommand(Guid ProjectId, string Title, string? Detail, string Priority = "Normal")
    : ICommand<WorkItemView>;
