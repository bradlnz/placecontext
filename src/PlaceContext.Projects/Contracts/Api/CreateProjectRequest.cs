namespace PlaceContext.Projects.Api;

/// <summary>Request body for POST /api/v1/projects.</summary>
public sealed record CreateProjectRequest(string Path, string? Name);
