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

public sealed record JobMessageDelivery(string Provider, string? ExternalId);
public sealed record JobEmailAttachment(string Name, string ContentType, string ContentBase64);

public interface IJobCrmClient
{
    Task<JobCrmCustomer?> GetCustomerAsync(Guid id, CancellationToken ct = default);
}

public sealed record JobCrmCustomer(
    Guid Id,
    string Name,
    string? Company,
    string? Email,
    string? Phone);

public interface IJobArtifactQueryClient
{
    Task<bool> HasHtmlReportAsync(Guid runId, CancellationToken ct = default);
}
