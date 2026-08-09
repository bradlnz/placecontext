namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one actionable focus item, derived from a project's logged state.</summary>
public sealed record FocusItem(
    string Kind,
    string Severity,
    string Title,
    string Detail,
    Guid ProjectId,
    string Project,
    string Url);
