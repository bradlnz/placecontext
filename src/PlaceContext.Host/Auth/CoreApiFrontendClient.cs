namespace PlaceContext.Host.Auth;

public sealed class CoreApiFrontendClient
{
    public string Id { get; set; } = "";
    public string Secret { get; set; } = "";
    public List<string> AllowedOrigins { get; set; } = [];
    public List<string> AllowedScopes { get; set; } = [];
}
