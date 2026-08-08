namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class JobRunSourceJson
{
    public string Kind { get; set; } = "image";
    public string? Image { get; set; }
    public string? RuntimeId { get; set; }
    public string? Source { get; set; }
    public string? Entrypoint { get; set; }
    public List<JobRunCodeFileJson>? Files { get; set; }
}
