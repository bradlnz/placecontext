namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one search hit, with where it links to.</summary>
public sealed record SearchHit(string Kind, Guid ProjectId, string Title, string Subtitle, string Url);
