namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the workspace's current focus checklist.</summary>
public sealed record FocusView(IReadOnlyList<FocusItem> Items, int ProjectCount);
