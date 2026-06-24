namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the workspace's current focus checklist (actionable items across all projects).</summary>
public sealed record FocusView(IReadOnlyList<FocusItem> Items, int ProjectCount);
