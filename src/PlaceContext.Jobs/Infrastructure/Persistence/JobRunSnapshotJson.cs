namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class JobRunSnapshotJson
{
    public JobRunSourceJson MapSource { get; set; } = new();
    public List<string>? InputPayloads { get; set; }
    public Dictionary<string, string>? MapEnv { get; set; }
    public JobRunSourceJson? ReduceSource { get; set; }
    public Dictionary<string, string>? ReduceEnv { get; set; }
    public int ConcurrencyLimit { get; set; } = 1;
    public bool AllowNetworkEgress { get; set; }
}
