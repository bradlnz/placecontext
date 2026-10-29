using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class EventsViewModelTests
{
    [Fact]
    public void Activity_filters_project_source_and_payload_without_confusing_workspace_events()
    {
        var project = Guid.NewGuid();
        var item = new EventOccurrenceView(Guid.NewGuid(), "job.completed", "Domain", project,
            "{\"status\":\"Failed\"}", DateTimeOffset.UtcNow, 0);
        Assert.True(EventsViewModel.MatchesActivity(item, project, "project", "Domain", "FAILED"));
        Assert.False(EventsViewModel.MatchesActivity(item, Guid.NewGuid(), "project", "", ""));
        Assert.True(EventsViewModel.MatchesActivity(item, Guid.NewGuid(), "workspace", "", "completed"));
        Assert.False(EventsViewModel.MatchesActivity(item, project, "workspace", "User", ""));
    }

    [Fact]
    public void Links_only_system_job_completion_events_to_valid_run_ids()
    {
        var runId = Guid.NewGuid();
        var item = new EventOccurrenceView(Guid.NewGuid(), "job.completed", "Domain", null,
            $"{{\"runId\":\"{runId}\",\"status\":\"Succeeded\"}}", DateTimeOffset.UtcNow, 0);
        Assert.Equal(runId, EventsViewModel.OriginRunId(item));
        Assert.Equal("Succeeded", EventsViewModel.Outcome(item));
        Assert.Null(EventsViewModel.OriginRunId(item with { Source = "User" }));
        Assert.Null(EventsViewModel.OriginRunId(item with { Payload = "{broken" }));
        Assert.Null(EventsViewModel.OriginRunId(item with { Payload = "[]" }));
        Assert.Null(EventsViewModel.OriginRunId(item with { Payload = "{\"runId\":\"javascript:bad\"}" }));
    }
}
