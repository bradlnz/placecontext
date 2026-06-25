namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the requirements a project must actually follow — global + the project's own, merged.</summary>
public sealed record EffectiveRequirementsView(Guid ProjectId, string Markdown, bool IsEmpty);
