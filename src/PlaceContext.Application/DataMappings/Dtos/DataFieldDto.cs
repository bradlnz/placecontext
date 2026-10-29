namespace PlaceContext.Application.Dtos;

/// <summary>One field of a data mapping: source dot-path → target column of a declared type.</summary>
public sealed record DataFieldDto(string SourcePath, string Column, string Type);
