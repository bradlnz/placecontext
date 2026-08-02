namespace PlaceContext.Infrastructure.OpenSearch;

public sealed class OpenSearchOptions
{
    public string? Endpoint { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string DefaultIndexPattern { get; set; } = "*";
    public string? SyncEndpoint { get; set; }
    public string? SyncToken { get; set; }
}
