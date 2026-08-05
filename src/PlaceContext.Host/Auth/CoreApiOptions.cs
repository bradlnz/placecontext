namespace PlaceContext.Host.Auth;

/// <summary>
/// Configuration for the engine/Core API. A request to /api/core/* must present one configured
/// frontend client identity + shared secret.
/// </summary>
public sealed class CoreApiOptions
{
    /// <summary>Header name for the calling frontend's client id.</summary>
    public string ClientIdHeader { get; set; } = "X-Core-Frontend-Id";

    /// <summary>Header name for the calling frontend's shared secret.</summary>
    public string ApiKeyHeader { get; set; } = "X-Core-Frontend-Key";

    /// <summary>Registered frontend clients accepted by the Core API.</summary>
    public List<CoreApiFrontendClient> Clients { get; set; } = [];
}

public sealed class CoreApiFrontendClient
{
    public string Id { get; set; } = "";
    public string Secret { get; set; } = "";
    public List<string> AllowedOrigins { get; set; } = [];
    public List<string> AllowedScopes { get; set; } = [];
}
