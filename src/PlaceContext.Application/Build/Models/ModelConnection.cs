namespace PlaceContext.Application.Build;

public sealed record ModelConnection(Guid Id, string Name, string Protocol, string BaseUrl,
    IReadOnlyList<string> Models, bool Enabled, bool HasApiKey)
{
    public bool IsLocalCli => Protocol is "codex" or "claude-code";
    public string? SetupHint => IsLocalCli ? Models.Count > 0 ? "Uses the CLI sign-in on this host; inference runs remotely."
        : $"No models available. Check {(Protocol == "codex" ? "codex login status" : "claude auth status")} on the app host." : null;
}
