using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class QueuesViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T03:00:00Z");

    [Fact]
    public void Queue_search_matches_name_workflow_and_id_with_intake_filter()
    {
        var queue = new ChainQueueDefinition(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "incoming-orders", true, Now, Now);
        Assert.True(QueuesViewModel.MatchesQueue(queue, "Review orders", "ORDERS", "enabled"));
        Assert.True(QueuesViewModel.MatchesQueue(queue, "Review orders", "REVIEW", ""));
        Assert.True(QueuesViewModel.MatchesQueue(queue, "Review orders", queue.Id.ToString(), ""));
        Assert.False(QueuesViewModel.MatchesQueue(queue, "Review orders", "orders", "paused"));
        Assert.False(QueuesViewModel.MatchesQueue(queue, "Review orders", "missing", ""));
    }

    [Theory]
    [InlineData("Queued", -1, 60, "Available")]
    [InlineData("Queued", 60, 120, "Delayed")]
    [InlineData("Queued", -60, -1, "Expired")]
    [InlineData("Running", -60, -1, "In flight")]
    [InlineData("AwaitingInput", -60, -1, "Awaiting input")]
    [InlineData("RetryPending", -60, 60, "Retry pending")]
    [InlineData("Succeeded", -60, -1, "Succeeded")]
    public void Monitoring_distinguishes_delayed_available_and_active_messages(string status, int available, int expires, string expected)
    {
        var message = new JobChainSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, 0, null, Now, null, null)
        { AvailableAt = Now.AddSeconds(available), ExpiresAt = Now.AddSeconds(expires) };
        Assert.Equal(expected, QueuesViewModel.MessageState(message, Now));
    }
}
