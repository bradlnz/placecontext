namespace PlaceContext.Host.Components.ViewModels;

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

    private ToolCallResult(
        bool success,
        string? data,
        string? error,
        bool isGraph,
        bool isMap,
        bool isArtifact
    )
    {
        Success = success;
        Data = data;
        Error = error;
        IsGraph = isGraph;
        IsMap = isMap;
        IsArtifact = isArtifact;
    }
}
