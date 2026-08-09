namespace PlaceContext.Application.Ports;

/// <summary>Outbound Slack Web API (chat.postMessage). Disabled when no bot token is configured.</summary>
public interface ISlackClient
{
    bool IsEnabled { get; }

    Task PostMessageAsync(string channel, string text, string? threadTs = null, CancellationToken ct = default);
}
