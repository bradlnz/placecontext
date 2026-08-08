namespace PlaceContext.Infrastructure.Comms;

internal sealed class SendGridSettings
{
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
