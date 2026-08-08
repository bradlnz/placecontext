using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Api;

/// <summary>A declared input field a job needs before it runs (prompted in a modal, or injected by an
/// event source).</summary>
public sealed record JobParameterRequest(
    string Name,
    string? Label = null,
    bool Required = true,
    /// <summary>"text" | "number" | "select" | "checkbox" | "file".</summary>
    string Type = "text",
    IReadOnlyList<string>? Options = null);
