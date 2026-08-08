namespace PlaceContext.Jobs.Contracts.Management;

/// <summary>A declared input field a job needs before it runs.</summary>
public sealed record JobParameterRequest(
    string Name,
    string? Label = null,
    bool Required = true,
    /// <summary>"text" | "number" | "select" | "checkbox" | "file".</summary>
    string Type = "text",
    IReadOnlyList<string>? Options = null);
