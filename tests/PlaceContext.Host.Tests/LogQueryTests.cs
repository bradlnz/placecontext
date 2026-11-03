using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Host.Tests;

public sealed class LogQueryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T03:00:00Z");
    private static QueryLogEvent Entry(string message, string level = "WARN", string? timestamp = "2026-10-25T02:30:00Z") => new(
        new RunReportView(Guid.NewGuid(), "Prepare records", "Default project",
            new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded", Now, Now, [], null,
                new("code", "python", null, null, null, 1, 1, false))),
        new JobLogEvent(1, "shard 0 / stderr", timestamp, level, message));

    [Theory]
    [InlineData("level = WARN AND message ~ retry", true)]
    [InlineData("level IN (ERROR, warn) AND job ~ 'prepare'", true)]
    [InlineData("level NOT IN (WARN, INFO)", false)]
    [InlineData("NOT (level = ERROR OR message !~ retry)", true)]
    [InlineData("level = INFO OR level = WARN AND status = Failed", false)]
    [InlineData("(level = INFO OR level = WARN) AND status = Succeeded", true)]
    [InlineData("project = 'Default project' AND stream ~ stderr", true)]
    [InlineData("level != ERROR", true)]
    [InlineData("timestamp >= -1h AND timestamp < '2026-10-25T03:00:00Z'", true)]
    [InlineData("timestamp >= -15m", false)]
    [InlineData("message ~ 'retry AND recover'", true)]
    [InlineData("message ~ 'he said \\\"retry\\\"'", false)]
    [InlineData("", true)]
    public void Evaluates_filters_with_precedence_and_quoted_values(string query, bool expected)
        => Assert.Equal(expected, new LogQuery(query, Now).Matches(Entry("retry AND recover")));

    [Theory]
    [InlineData("message contains \"RETRY\"", true)]
    [InlineData("message !contains \"RETRY\"", false)]
    [InlineData("message not contains \"missing\"", true)]
    [InlineData("message contains_cs \"retry\"", true)]
    [InlineData("message contains_cs \"RETRY\"", false)]
    [InlineData("message !contains_cs \"RETRY\"", true)]
    [InlineData("message !contains_cs \"retry\"", false)]
    [InlineData("message NOT contains_cs \"retry\"", false)]
    [InlineData("message contains \"\"", true)]
    [InlineData("message !contains \"\"", false)]
    [InlineData("message contains \"try AND rec\"", true)]
    [InlineData("NOT (message !contains \"recover\") AND level = WARN", true)]
    public void Azure_contains_operators_match_substrings_with_explicit_case_behavior(string query, bool expected)
        => Assert.Equal(expected, new LogQuery(query, Now).Matches(Entry("retry AND recover")));

    [Theory]
    [InlineData("message contains")]
    [InlineData("message !contains")]
    [InlineData("message contains_cs")]
    [InlineData("timestamp contains \"2026\"")]
    [InlineData("message NOT unknown \"text\"")]
    [InlineData("job =")]
    [InlineData("unknown = value")]
    [InlineData("level IN ()")]
    [InlineData("level IN (WARN,)")]
    [InlineData("level = WARN OR")]
    [InlineData("(level = WARN")]
    [InlineData("message ~ 'unclosed")]
    [InlineData("job > something")]
    [InlineData("timestamp ~ now")]
    [InlineData("timestamp >= nonsense")]
    [InlineData("level = WARN ORDER BY secret")]
    [InlineData("level = WARN ORDER BY timestamp DESC junk")]
    public void Rejects_invalid_queries_without_silently_broadening_results(string query)
        => Assert.Throws<ArgumentException>(() => new LogQuery(query, Now));

    [Fact]
    public void Limits_complexity_and_keeps_unstamped_events_out_of_time_comparisons()
    {
        Assert.Throws<ArgumentException>(() => new LogQuery(new string('x', 4097), Now));
        Assert.Throws<ArgumentException>(() => new LogQuery(new string('(', 17) + "level = WARN" + new string(')', 17), Now));
        Assert.False(new LogQuery("timestamp != -1h", Now).Matches(Entry("no time", timestamp: null)));
    }

    [Fact]
    public void Orders_times_chronologically_and_leaves_missing_timestamps_last()
    {
        var older = Entry("older", timestamp: "2026-10-25T12:00:00+10:00");
        var newer = Entry("newer", timestamp: "2026-10-25T02:30:00Z");
        var unknown = Entry("unknown", timestamp: null);
        Assert.Equal(["newer", "older", "unknown"], new LogQuery("ORDER BY timestamp DESC", Now)
            .Sort([unknown, older, newer]).Select(row => row.Event.Message));
        Assert.Equal(["older", "newer", "unknown"], new LogQuery("ORDER BY timestamp ASC", Now)
            .Sort([unknown, newer, older]).Select(row => row.Event.Message));
    }

    [Fact]
    public void Inspecting_a_match_includes_unmatched_neighbors_but_never_other_runs_or_streams()
    {
        var vm = new ObservabilityViewModel(null!, null!, null!, null!, null!, new InMemoryWorkloadOutputBuffer(), null!);
        var report = Entry("").Report!;
        var lines = Enumerable.Range(1, 30).Select(i => i == 15
            ? "{\"level\":\"ERROR\",\"message\":\"failed\",\"request\":{\"id\":\"abc\"}}" : $"INFO line {i}");
        report = report with
        {
            Run = report.Run with
            {
                ShardResults = [new(0, 0, "Succeeded", null,
            string.Join('\n', lines), []), new(1, 0, "Succeeded", null, "INFO other shard", [])]
            }
        };
        var other = report with
        {
            Run = report.Run with
            {
                Id = Guid.NewGuid()
            }
        };
        vm.SetLogResults([report, other], new LogQuery($"run = '{report.Run.Id}' AND level = ERROR", Now));
        var match = Assert.Single(vm.LogResults!);
        vm.ToggleLogEvent(match);
        Assert.Same(match, vm.SelectedLog);
        Assert.Equal("abc", vm.SelectedLogFields!.Cell("json.request.id"));
        Assert.Equal(Enumerable.Range(5, 21), vm.LogContext.Select(entry => entry.Event.Index));
        Assert.All(vm.LogContext, entry =>
        {
            Assert.Equal(report.Run.Id, entry.Report!.Run.Id);
            Assert.Equal("shard 0", entry.Event.Stream);
        });
        Assert.Contains(vm.LogContext, entry => entry.Event.Level == "INFO");
        vm.ToggleLogEvent(match);
        Assert.Null(vm.SelectedLog);
        Assert.Null(vm.SelectedLogFields);
        Assert.Empty(vm.LogContext);
        vm.ToggleLogEvent(match);
        vm.SetLogResults([report], new LogQuery("level = INFO", Now));
        Assert.Null(vm.SelectedLog);
        vm.ToggleLogEvent(match); // A stale result cannot reopen against a refreshed snapshot.
        Assert.Null(vm.SelectedLog);
    }

    [Theory]
    [InlineData(0, 1, 11)]
    [InlineData(29, 20, 11)]
    public void Context_stops_at_source_boundaries(int selected, int first, int count)
    {
        var vm = new ObservabilityViewModel(null!, null!, null!, null!, null!, new InMemoryWorkloadOutputBuffer(), null!);
        var report = Entry("").Report!;
        report = report with
        {
            Run = report.Run with
            {
                ShardResults = [new(0, 0, "Succeeded", null,
            string.Join('\n', Enumerable.Range(1, 30).Select(i => $"INFO line {i}")), [])]
            }
        };
        vm.SetLogResults([report], new LogQuery("", Now));
        vm.ToggleLogEvent(vm.LogResults![selected]);
        Assert.Equal(Enumerable.Range(first, count), vm.LogContext.Select(entry => entry.Event.Index));
    }

    [Fact]
    public void Search_uses_live_output_and_persisted_logs_and_limits_only_displayed_matches()
    {
        var buffer = new InMemoryWorkloadOutputBuffer();
        var vm = new ObservabilityViewModel(null!, null!, null!, null!, null!, buffer, null!);
        var saved = Entry("").Report;
        saved = saved with
        {
            Run = saved.Run with
            {
                ShardResults = [new(0, 0, "Succeeded", null,
            string.Join('\n', Enumerable.Range(0, 2100).Select(i => $"WARN saved {i}")), [])]
            }
        };
        var live = Entry("").Report;
        live = live with
        {
            Run = live.Run with
            {
                Status = "Running"
            }
        };
        buffer.Set($"{live.Run.Id:N}-map-0", "", "ERROR live failure\n");
        vm.SetLogResults([saved, live], new LogQuery("level IN (WARN, ERROR)", Now));
        Assert.Equal(2, vm.LogRunCount);
        Assert.True(vm.LogSourcesTruncated);
        Assert.Equal(2001, vm.LogMatchCount);
        Assert.Equal(500, vm.LogResults!.Count);
        vm.SetLogResults([saved, live], new LogQuery($"run = '{live.Run.Id}'", Now));
        Assert.Equal("ERROR live failure", Assert.Single(vm.LogResults!).Event.Message);
    }
}
