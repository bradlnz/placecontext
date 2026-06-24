namespace PlaceContext.Application.Dtos;

/// <summary>Read model: results of a workspace search across projects, changes, context, and decisions.</summary>
public sealed record SearchResultsView(string Term, IReadOnlyList<SearchHit> Hits);
