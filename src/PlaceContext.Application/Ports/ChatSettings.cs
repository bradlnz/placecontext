namespace PlaceContext.Application.Ports;

/// <summary>Optional generation parameters forwarded to the model backend.</summary>
public sealed record ChatSettings(
    float? Temperature = null,
    float? TopP = null,
    int? MaxTokens = null);
