namespace PlaceContext.Application.Dtos;

/// <summary>A declared input field a job needs before it runs (prompted in a modal, or injected by an
/// event source). Transport shape for <see cref="PlaceContext.Domain.ValueObjects.JobParameter"/>.</summary>
public sealed record JobParameterDto(string Name, string? Label = null, bool Required = true,
    /// <summary>"text" | "number" | "select" | "checkbox" — how run forms render this input.</summary>
    string Type = "text",
    /// <summary>Choices offered when Type is "select".</summary>
    IReadOnlyList<string>? Options = null);
