namespace PlaceContext.Application.Dtos;

public sealed record ImprovementsView(Guid ProjectId, IReadOnlyList<ImprovementView> Items);
