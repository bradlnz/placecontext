namespace PlaceContext.Application.Ports;

/// <summary>
/// Maps a Slack conversation thread to a PlaceContext chat session id so multi-turn threads
/// share memory with /chat.
/// </summary>
public interface ISlackThreadSessionStore
{
    /// <summary>Returns the existing session for the thread, or allocates a new id and stores it.</summary>
    Task<Guid> GetOrCreateSessionIdAsync(string teamId, string channelId, string threadRootTs, CancellationToken ct = default);

    /// <summary>True if this Slack event id was already claimed (dedupe retries).</summary>
    Task<bool> TryClaimEventAsync(string eventId, CancellationToken ct = default);
}
