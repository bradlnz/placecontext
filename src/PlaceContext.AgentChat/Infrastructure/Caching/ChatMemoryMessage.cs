namespace PlaceContext.AgentChat.Infrastructure.Caching;

/// <summary>One message in the conversation memory.</summary>
public sealed record ChatMemoryMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    List<ChatMemoryToolCall>? ToolCalls = null,
    string? AttachmentName = null,
    string? AttachmentKey = null,
    string? AttachmentContentType = null,
    long AttachmentSizeBytes = 0,
    string? Thinking = null,
    bool AttachmentParsed = false,
    int AttachmentExtractedChars = 0);
