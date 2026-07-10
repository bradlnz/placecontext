namespace PlaceContext.Application.Dtos;

/// <summary>
/// Read/write model for a single source file in a code workload — a relative path and its content.
/// Used by the Jobs editor and the upload_job_code MCP tool. Content is opaque.
/// </summary>
public sealed record CodeFileDto(string Path, string Content);
