using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Promote a discovered project (or register a path directly) to Registered.</summary>
public sealed record RegisterProjectCommand(Guid ProjectId) : ICommand<ProjectSummaryView>;
