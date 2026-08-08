namespace PlaceContext.Infrastructure.Comms;

public sealed class ClientCommsOptions
{
    public const string SectionName = "PlaceContext:Comms";
    public PostmarkOptions Postmark { get; set; } = new();
}
