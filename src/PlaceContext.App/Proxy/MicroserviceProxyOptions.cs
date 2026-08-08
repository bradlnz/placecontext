namespace PlaceContext.App.Proxy;

public sealed class MicroserviceProxyOptions
{
    public const string SectionName = "PlaceContext:Microservices";

    public Dictionary<string, string> Destinations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
