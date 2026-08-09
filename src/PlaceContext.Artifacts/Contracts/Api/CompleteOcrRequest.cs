namespace PlaceContext.Artifacts.Contracts.Api;

/// <summary>Body of <c>POST /api/ocr/complete</c>. Exactly one of Markdown/Error describes the attempt.</summary>
public sealed record CompleteOcrRequest(Guid ArtifactId, string? Markdown, string? Error);
