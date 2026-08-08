namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class JobRunShardResultJson
{
    public int Index { get; set; }
    public int ExitCode { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Artifact { get; set; }
    public string? Log { get; set; }
    public List<JobRunArtifactJson>? Artifacts { get; set; }
}
