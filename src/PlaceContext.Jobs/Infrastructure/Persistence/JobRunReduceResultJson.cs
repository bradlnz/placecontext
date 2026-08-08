namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class JobRunReduceResultJson
{
    public int ExitCode { get; set; }
    public bool Succeeded { get; set; }
    public string? Artifact { get; set; }
    public string? Log { get; set; }
    public List<JobRunArtifactJson>? Artifacts { get; set; }
}
