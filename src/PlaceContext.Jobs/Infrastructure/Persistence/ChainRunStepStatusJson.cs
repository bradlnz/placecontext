namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class ChainRunStepStatusJson
{
    public string JobName { get; set; } = string.Empty;
    public Guid? RunId { get; set; }
    public string Status { get; set; } = string.Empty;
}
