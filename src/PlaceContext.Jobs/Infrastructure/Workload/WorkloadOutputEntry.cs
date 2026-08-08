using System.Text;

namespace PlaceContext.Jobs.Infrastructure.Workload;

internal sealed class WorkloadOutputEntry
{
    public object Gate { get; } = new();
    public StringBuilder Stdout { get; } = new();
    public StringBuilder Stderr { get; } = new();
    public bool IsComplete { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
