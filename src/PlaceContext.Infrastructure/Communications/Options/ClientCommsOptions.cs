namespace PlaceContext.Infrastructure.Comms;

public sealed class ClientCommsOptions
{
    public const string SectionName = "PlaceContext:Comms";
    public TransactionalEmailOptions Postmark { get; set; } = new();
}
