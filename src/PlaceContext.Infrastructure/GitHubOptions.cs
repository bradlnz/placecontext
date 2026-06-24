namespace PlaceContext.Infrastructure;

/// <summary>GitHub OAuth app credentials. Supply via env/user-secrets; empty by default.</summary>
public sealed class GitHubOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}
