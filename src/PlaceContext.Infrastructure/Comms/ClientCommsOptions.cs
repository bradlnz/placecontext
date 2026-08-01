namespace PlaceContext.Infrastructure.Comms;

public sealed class ClientCommsOptions
{
    public const string SectionName = "PlaceContext:Comms";
    public PostmarkOptions Postmark { get; set; } = new();
}

public sealed class PostmarkOptions
{
    public string ApiEndpoint { get; set; } = "https://api.postmarkapp.com";
}
