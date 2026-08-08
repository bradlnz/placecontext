namespace PlaceContext.Jobs.Infrastructure.Workload;

/// <summary>
/// Defines how a generic runtime sandbox is launched.
/// The <see cref="InvokeCommand"/> array is used as the docker CMD override;
/// the token <c>{entrypoint}</c> is replaced with the effective entry-point filename.
/// </summary>
public sealed class RuntimeDefinition
{
    /// <summary>Base container image (e.g. "node:22-slim", "python:3.12-slim").</summary>
    public string BaseImage { get; set; } = "";

    /// <summary>
    /// Command array template (docker CMD override). Use <c>{entrypoint}</c> as a placeholder.
    /// Example: ["node", "/work/{entrypoint}"]
    /// </summary>
    public string[] InvokeCommand { get; set; } = Array.Empty<string>();

    /// <summary>Default entry-point filename when the job doesn't specify one (e.g. "index.js").</summary>
    public string DefaultEntrypoint { get; set; } = "";
}
