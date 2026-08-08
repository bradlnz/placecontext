namespace PlaceContext.Infrastructure.Comms;

internal sealed class TwilioSettings
{
    public string AccountSid { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
