namespace PlaceContext.Application.Dtos;

/// <summary>
/// Read model for a named output file (e.g. report.csv) produced by a run step.
/// When <paramref name="IsBinary"/> is set, <paramref name="Content"/> is base64 of the file bytes.
/// </summary>
public sealed record RunArtifactView(string Name, string Content, bool IsBinary = false);
