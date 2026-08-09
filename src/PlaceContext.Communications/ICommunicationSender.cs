using PlaceContext.Communications.Contracts;

namespace PlaceContext.Communications;

public interface ICommunicationSender
{
    Task<CommunicationCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
    Task<CommunicationDelivery> SendEmailAsync(
        SendCommunicationEmailRequest request,
        CancellationToken ct = default);
    Task<CommunicationDelivery> SendSmsAsync(
        SendCommunicationSmsRequest request,
        CancellationToken ct = default);
    Task<CommunicationDelivery> SendTestAsync(
        Guid providerId,
        string recipient,
        CancellationToken ct = default);
}
