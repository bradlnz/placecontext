namespace PlaceContext.Infrastructure.Comms;

internal sealed class PostmarkSettings
{
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string MessageStream { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
