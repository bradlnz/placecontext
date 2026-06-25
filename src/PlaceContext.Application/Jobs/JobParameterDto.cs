namespace PlaceContext.Application.Dtos;

/// <summary>A declared input field a job needs before it runs (prompted in a modal, or injected by an
/// event source). Transport shape for <see cref="PlaceContext.Domain.ValueObjects.JobParameter"/>.</summary>
public sealed record JobParameterDto(string Name, string? Label = null, bool Required = true);
