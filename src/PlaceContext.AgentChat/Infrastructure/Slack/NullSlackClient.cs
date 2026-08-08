using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Infrastructure.Slack;

public sealed class NullSlackClient : ISlackClient
{
    public bool IsEnabled => false;

    public Task PostMessageAsync(string channel, string text, string? threadTs = null, CancellationToken ct = default)
        => Task.CompletedTask;
}
