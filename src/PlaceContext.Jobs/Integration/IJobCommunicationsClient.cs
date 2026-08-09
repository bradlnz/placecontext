namespace PlaceContext.Jobs.Integration;

public interface IJobCommunicationsClient
{
    string EmailProvider { get; }
    string SmsProvider { get; }
    Task<JobMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<JobEmailAttachment>? attachments = null);
    Task<JobMessageDelivery> SendSmsAsync(string recipient, string body, CancellationToken ct = default);
}
