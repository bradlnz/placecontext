namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one suggested improvement, derived from logged activity (no LLM).</summary>
public sealed record ImprovementView(string Code, string Severity, string Title, string Detail);
