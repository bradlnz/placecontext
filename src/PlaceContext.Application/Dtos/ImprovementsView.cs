namespace PlaceContext.Application.Dtos;

/// <summary>Read model: prioritized improvement suggestions for a project.</summary>
public sealed record ImprovementsView(Guid ProjectId, IReadOnlyList<ImprovementView> Items);
