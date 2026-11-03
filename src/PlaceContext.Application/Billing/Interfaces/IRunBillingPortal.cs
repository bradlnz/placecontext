namespace PlaceContext.Application.Billing;

public interface IRunBillingPortal
{
    Uri? EnterpriseSalesUrl
    {
        get;
    }
    Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default);
    Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default);
}
