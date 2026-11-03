namespace PlaceContext.Infrastructure.Search;

public sealed class SearchOptions
{
    public string? Endpoint
    {
        get; set;
    }
    public string? Username
    {
        get; set;
    }
    public string? Password
    {
        get; set;
    }
    public string DefaultIndexPattern { get; set; } = "*";
    public string? SyncEndpoint
    {
        get; set;
    }
    public string? SyncToken
    {
        get; set;
    }
}
