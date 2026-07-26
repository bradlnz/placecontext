namespace PlaceContext.Host.Components.ViewModels;

// ── Chat message and tool-call models ────────────────────────────────────────

public sealed class AgentMessage
{
    private static int _nextId;
    public AgentMessage(string role, string content)
    {
        Id = Interlocked.Increment(ref _nextId);
        Role = role;
        Content = content;
    }
    public int Id { get; }
    public string Role { get; }
    public string Content { get; }
    public string? Thinking { get; set; }
    public List<ToolCallInfo> ToolCalls { get; } = new();
    public string? AttachmentName { get; set; }
    public string? AttachmentKey { get; set; }
    public string? AttachmentContentType { get; set; }
    public long AttachmentSizeBytes { get; set; }
}

public sealed class ToolCallInfo
{
    private static int _nextId;
    public int Id { get; set; } = Interlocked.Increment(ref _nextId);
    public string ToolName { get; set; } = "";
    public string Args { get; set; } = "";
    public AgentToolCallStatus Status { get; set; }
    public string? Result { get; set; }
    public string ResultType { get; set; } = "text";
    public int RetryCount { get; set; }
}

public enum AgentToolCallStatus { Pending, Running, Completed, Error }

public sealed class ToolCallResult
{
    public bool Success { get; }
    public string? Data { get; }
    public string? Error { get; }
    public bool IsGraph { get; }
    public bool IsMap { get; }
    public bool IsArtifact { get; }

    public static ToolCallResult Ok(string data) => new(true, data, null, false, false, false);
    public static ToolCallResult Fail(string error) => new(false, null, error, false, false, false);
    public static ToolCallResult Graph(string data) => new(true, data, null, true, false, false);
    public static ToolCallResult Map(string data) => new(true, data, null, false, true, false);
    public static ToolCallResult Artifact(string data) => new(true, data, null, false, false, true);

    private ToolCallResult(bool success, string? data, string? error, bool isGraph, bool isMap, bool isArtifact)
    {
        Success = success;
        Data = data;
        Error = error;
        IsGraph = isGraph;
        IsMap = isMap;
        IsArtifact = isArtifact;
    }
}

public sealed class AgentAction
{
    public string ToolName { get; set; } = "";
    public string Detail { get; set; } = "";
    public AgentToolCallStatus Status { get; set; }
}

public sealed class FetchedData
{
    public string Source { get; set; } = "";
    public int RowCount { get; set; }
    public string Preview { get; set; } = "";
}

public sealed class ToolHistoryEntry
{
    public string ToolName { get; set; } = "";
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class ClarificationRequest
{
    public string ToolName { get; set; } = "";
    public string Args { get; set; } = "";
    public string Question { get; set; } = "";
    public List<ClarificationOption> Options { get; set; } = new();
    public bool MultiSelect { get; set; }
}

public sealed class ClarificationOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
}

public sealed class ClarificationResult
{
    public bool Confirmed { get; set; }
    public List<string> SelectedIds { get; set; } = new();
}

public sealed class UploadedFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
