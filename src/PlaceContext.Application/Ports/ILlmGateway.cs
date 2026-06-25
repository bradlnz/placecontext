namespace PlaceContext.Application.Ports;

/// <summary>
/// Optional generation backend used to polish a deterministically-assembled report into a fluent
/// narrative. The platform works without it: when <see cref="IsEnabled"/> is false (no API key
/// configured) the report falls back to the assembled Markdown unchanged.
/// </summary>
public interface ILlmGateway
{
    /// <summary>Whether a real generation backend is configured. When false, callers skip the polish pass.</summary>
    bool IsEnabled { get; }

    /// <summary>Generates text from a system instruction plus user content. Only called when <see cref="IsEnabled"/>.</summary>
    Task<string> GenerateAsync(string system, string user, CancellationToken ct = default);
}
