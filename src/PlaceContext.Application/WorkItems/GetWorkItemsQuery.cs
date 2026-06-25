using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>List a project's work-queue items.</summary>
public sealed record GetWorkItemsQuery(Guid ProjectId) : IQuery<IReadOnlyList<WorkItemView>>;
