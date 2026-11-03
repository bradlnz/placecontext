using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Host.Tests;

public class JobRunLogsViewModelTests
{
    [Theory]
    [InlineData("2026-10-25T01:02:03Z [ERROR] failed", "2026-10-25T01:02:03Z", "ERROR")]
    [InlineData("{\"timestamp\":\"2026-10-25T01:02:03Z\",\"level\":\"warning\",\"message\":\"retry\"}", "2026-10-25T01:02:03Z", "WARN")]
    [InlineData("INFO ready", null, "INFO")]
    [InlineData("there was no error", null, "")]
    [InlineData("{invalid json", null, "")]
    [InlineData("<script>alert('log')</script>", null, "")]
    public void Parses_only_emitted_metadata_and_preserves_the_original_line(string line, string? time, string level)
    {
        var entry = JobRunLogsViewModel.Parse(1, "shard 0", line);
        Assert.Equal(time, entry.Timestamp);
        Assert.Equal(level, entry.Level);
        Assert.Equal(line, entry.Message);
    }

    [Fact]
    public void Live_updates_pause_resume_and_switch_to_durable_streams_when_finished()
    {
        var buffer = new InMemoryWorkloadOutputBuffer();
        var vm = new JobRunLogsViewModel(buffer);
        var run = Run("Running");
        buffer.Set($"{run.Id:N}-map-0", "INFO started\n", "WARN retry\n");
        vm.SetRun(run);
        Assert.Equal(2, vm.Events.Count);
        Assert.EndsWith("stderr", vm.Events[1].Stream);
        vm.TogglePause(run);
        buffer.Append($"{run.Id:N}-map-0", "INFO finished\n");
        vm.SetRun(run);
        Assert.Equal(2, vm.Events.Count);
        vm.TogglePause(run);
        Assert.Equal(3, vm.Events.Count);
        vm.Search = "RETRY";
        vm.Level = "WARN";
        Assert.Single(vm.Filtered);
        vm.SetRun(run with
        {
            Status = "Succeeded",
            ShardResults = [new(0, 0, "Succeeded", null, "saved\n--- stderr ---\nWARN retry", [])]
        });
        Assert.False(vm.IsLive);
        Assert.Equal("shard 0 / stderr", Assert.Single(vm.Filtered).Stream);
        Assert.Equal("saved", vm.Events[0].Message);
    }

    [Fact]
    public void Unchanged_sources_reuse_parsed_events_but_new_output_is_loaded()
    {
        var vm = new JobRunLogsViewModel(new InMemoryWorkloadOutputBuffer());
        var run = Run("Succeeded") with
        {
            ShardResults = [new(0, 0, "Succeeded", null, "INFO original", [])]
        };
        vm.SetRun(run);
        var events = vm.Events;
        vm.SetRun(run with
        {
            FinishedAt = DateTimeOffset.UtcNow
        });
        Assert.Same(events, vm.Events);
        vm.SetRun(run with
        {
            ShardResults = [new(0, 0, "Succeeded", null, "INFO changed", [])]
        });
        Assert.Equal("INFO changed", Assert.Single(vm.Events).Message);
    }

    [Fact]
    public void Large_logs_are_bounded_and_switching_runs_clears_paused_stream_selection()
    {
        var vm = new JobRunLogsViewModel(new InMemoryWorkloadOutputBuffer());
        var run = Run("Succeeded") with
        {
            ShardResults = [new(0, 0, "Succeeded", null, string.Join('\n', Enumerable.Range(1, 2100)), [])],
            ReduceResult = new(0, true, null, "INFO reduced", []),
        };
        vm.SetRun(run);
        Assert.Equal(2000, vm.Events.Count);
        Assert.True(vm.Truncated);
        Assert.Contains("\n1\n2\n", vm.FullLog);
        Assert.Contains("INFO reduced", vm.FullLog);
        Assert.Equal("INFO reduced", vm.Events[^1].Message);
        vm.Stream = "reduce";
        Assert.Single(vm.Filtered);
        vm.TogglePause(run);
        vm.SetRun(Run("Running"));
        Assert.False(vm.Paused);
        Assert.Empty(vm.Stream);
        Assert.Empty(vm.Events);
    }

    private static JobRunDetailView Run(string status) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        status, DateTimeOffset.UtcNow, null, [], null, new("code", "python", null, null, null, 1, 1, false));
}
