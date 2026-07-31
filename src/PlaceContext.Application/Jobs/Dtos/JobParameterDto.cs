namespace PlaceContext.Application.Dtos;

/// <summary>A declared input field a job needs before it runs (prompted in a modal, or injected by an
/// event source). Transport shape for <see cref="PlaceContext.Domain.ValueObjects.JobParameter"/>.</summary>
public sealed record JobParameterDto(string Name, string? Label = null, bool Required = true,
    /// <summary>"text" | "number" | "select" | "checkbox" | "file".</summary>
    string Type = "text",
    /// <summary>Select choices, or accepted MIME/extension filters when Type is "file".</summary>
    IReadOnlyList<string>? Options = null);
