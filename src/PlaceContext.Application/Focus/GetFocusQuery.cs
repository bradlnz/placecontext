using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>The workspace's current focus checklist — top actionable items across all projects.</summary>
public sealed record GetFocusQuery(int Limit = 12) : IQuery<FocusView>;
