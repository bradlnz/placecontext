using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>The change timeline for one project, newest first.</summary>
public sealed record GetTimelineQuery(Guid ProjectId, int Take = 50) : IQuery<ChangeTimelineView>;
