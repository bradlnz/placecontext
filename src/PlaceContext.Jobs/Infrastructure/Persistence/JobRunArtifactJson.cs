namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class JobRunArtifactJson
{
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>Null for text artifacts so legacy JSON remains compatible.</summary>
    public bool? IsBinary { get; set; }
}
