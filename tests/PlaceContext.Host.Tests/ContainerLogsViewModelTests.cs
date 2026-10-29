using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ContainerLogsViewModelTests
{
    [Fact]
    public void Container_logs_preserve_streams_and_parse_structured_levels_after_docker_timestamps()
    {
        var vm = new ContainerLogsViewModel();
        vm.SetText("── stdout ──\n2026-10-26T01:00:00.123456789Z {\"level\":\"info\",\"message\":\"Ready\"}\n── stderr ──\n2026-10-26T01:01:00Z ERROR broken\n");
        Assert.Equal(2, vm.Events.Count);
        Assert.Equal("stdout", vm.Events[0].Stream);
        Assert.Equal("INFO", vm.Events[0].Level);
        Assert.StartsWith("{", vm.Events[0].Message);
        Assert.Equal("stderr", vm.Events[1].Stream);
        vm.Level = "ERROR";
        Assert.Equal("ERROR broken", Assert.Single(vm.Filtered).Message);
        vm.Search = "absent";
        Assert.Empty(vm.Filtered);
    }

    [Fact]
    public void Service_log_events_support_the_existing_query_engine_without_a_fake_job_run()
    {
        var source = new ServiceLogSource(Guid.NewGuid(), Guid.NewGuid(), "Portal", "Demo", "running");
        var entries = ContainerLogsViewModel.Parse("2026-10-26T01:00:00Z {\"level\":\"info\",\"visits\":4}");
        var result = new LogAnalyticsQuery("filter source = service | fields service, json.visits, level | limit 10", DateTimeOffset.UtcNow)
            .Execute(entries.Select(entry => new QueryLogEvent(null, entry, source)));
        var row = Assert.Single(result.Rows);
        Assert.Equal("Portal", row.Cell("service"));
        Assert.Equal("4", row.Cell("json.visits"));
        Assert.Equal("INFO", row.Cell("level"));
        Assert.Null(row.Source!.Report);
        Assert.Equal(source.Id, row.Source.Service!.Id);
    }
}
