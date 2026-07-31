namespace PlaceContext.Application.Ports;

public sealed record ClientCommsCapabilities(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);

public sealed record ClientMessageDelivery(string Provider, string? ExternalId);

/// <summary>Provider-neutral email attachment. Content is RFC 4648 base64.</summary>
public sealed record ClientEmailAttachment(string Name, string ContentType, string ContentBase64);

public interface IClientCommunicationSender
{
    string EmailProvider { get; }
    string SmsProvider { get; }
    Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
    Task<ClientMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<ClientEmailAttachment>? attachments = null);
    Task<ClientMessageDelivery> SendAuthenticationEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default);
    Task<ClientMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default);
}
