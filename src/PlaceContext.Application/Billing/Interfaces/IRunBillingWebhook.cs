namespace PlaceContext.Application.Billing;

public interface IRunBillingWebhook
{
    Task ApplyAsync(string body, string signature, CancellationToken ct = default);
}
