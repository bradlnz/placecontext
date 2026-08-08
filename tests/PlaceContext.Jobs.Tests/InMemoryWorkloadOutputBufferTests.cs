using PlaceContext.Jobs.Infrastructure.Workload;
using Xunit;

namespace PlaceContext.Jobs.Tests;

public sealed class InMemoryWorkloadOutputBufferTests
{
    [Fact]
    public void Snapshot_combines_stdout_and_stderr_for_a_live_run()
    {
        var runId = Guid.NewGuid();
        var buffer = new InMemoryWorkloadOutputBuffer();
        var correlationId = $"{runId:N}-map-0";

        buffer.Append(correlationId, "processing 1/2\n");
        buffer.Append(correlationId, "retrying\n", isError: true);

        var snapshot = Assert.IsType<PlaceContext.Application.Ports.LiveWorkloadOutput>(
            buffer.Snapshot(runId));
        Assert.Contains("processing 1/2", snapshot.Text);
        Assert.Contains("[stderr]", snapshot.Text);
        Assert.Contains("retrying", snapshot.Text);
        Assert.False(snapshot.IsComplete);
    }

    [Fact]
    public void Snapshot_groups_parallel_map_streams_and_reports_completion()
    {
        var runId = Guid.NewGuid();
        var buffer = new InMemoryWorkloadOutputBuffer();
        var first = $"{runId:N}-map-0";
        var second = $"{runId:N}-map-1";

        buffer.Set(first, "first");
        buffer.Set(second, "second");
        buffer.Complete(first);
        buffer.Complete(second);

        var snapshot = buffer.Snapshot(runId)!;
        Assert.Contains("map 0", snapshot.Text);
        Assert.Contains("map 1", snapshot.Text);
        Assert.Contains("first", snapshot.Text);
        Assert.Contains("second", snapshot.Text);
        Assert.True(snapshot.IsComplete);
    }
}
