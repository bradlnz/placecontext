namespace PlaceContext.AgentChat.Infrastructure.Slack;

/// <summary>Bound from <c>PlaceContext:Slack</c>. Disabled when signing secret or bot token is blank.</summary>
public sealed class SlackOptions
{
    public const string SectionName = "PlaceContext:Slack";

    /// <summary>Slack app Signing Secret (Events API request verification).</summary>
    public string SigningSecret { get; set; } = "";

    /// <summary>Bot User OAuth Token (<c>xoxb-…</c>) for chat.postMessage.</summary>
    public string BotToken { get; set; } = "";

    /// <summary>Project the Slack agent acts in (required when enabled).</summary>
    public string ProjectId { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SigningSecret)
        && !string.IsNullOrWhiteSpace(BotToken)
        && Guid.TryParse(ProjectId, out var id) && id != Guid.Empty;
}
