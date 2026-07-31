namespace PlaceContext.Application.Ports;

public sealed record ClientCommsCapabilities(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);

public sealed record ClientMessageDelivery(string Provider, string? ExternalId);

public interface IClientCommunicationSender
{
    ClientCommsCapabilities Capabilities { get; }
    Task<ClientMessageDelivery> SendEmailAsync(
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
