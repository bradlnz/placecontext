namespace PlaceContext.Application.Communications;

public interface ICommunicationTestSender
{
    Task<ClientMessageDelivery> SendTestAsync(Guid providerId, string recipient, CancellationToken ct = default);
}
