using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Billing;

/// <summary>Stripe-hosted subscription checkout and billing portal. Prices are configured on the
/// server and never accepted as arbitrary client-supplied IDs.</summary>
public sealed class PaymentBillingPortal(
    IHttpClientFactory clients, IOptions<PaymentBillingOptions> settings,
    AppDbContext db, ICurrentTenant tenant, IPermissionService permissions) : IRunBillingPortal
{
    public Uri? EnterpriseSalesUrl =>
        Uri.TryCreate(settings.Value.EnterpriseSalesUrl, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps ? url : null;

    public Task<Uri> CheckoutAsync(string plan, Uri returnBase, CancellationToken ct = default)
        => PostAsync("checkout", plan, returnBase, ct);

    public Task<Uri> PortalAsync(Uri returnBase, CancellationToken ct = default)
        => PostAsync("portal", null, returnBase, ct);

    private async Task<Uri> PostAsync(string type, string? plan, Uri returnBase, CancellationToken ct)
    {
        if (!tenant.IsResolved || !await permissions.HasAsync(Permission.SettingsManage, ct))
        {
            throw new UnauthorizedAccessException("Billing administration is required.");
        }
        if (returnBase.Scheme != Uri.UriSchemeHttps
                    && !(returnBase.Scheme == Uri.UriSchemeHttp && returnBase.IsLoopback)
                    || !string.IsNullOrEmpty(returnBase.UserInfo)
                    || !string.IsNullOrEmpty(returnBase.Query) || !string.IsNullOrEmpty(returnBase.Fragment))
        {
            throw new InvalidOperationException("Billing requires an HTTPS workspace origin.");
        }
        var config = settings.Value;
        if (string.IsNullOrWhiteSpace(config.SecretKey))
        {
            throw new InvalidOperationException("Stripe billing is not configured.");
        }
        var current = await db.TenantRunPlans.AsNoTracking().SingleOrDefaultAsync(ct);
        var price = plan is null ? null : config.PriceFor(plan);
        if (type == "checkout" && string.IsNullOrWhiteSpace(price))
        {
            throw new ArgumentException("This plan is unavailable.", nameof(plan));
        }
        if (type == "checkout" && current is { StripeSubscriptionId: not null, Status: "active" or "trialing" })
        {
            throw new InvalidOperationException(
                        "A subscription is already active. Use Manage subscription to change plans.");
        }
        if (type == "portal" && string.IsNullOrWhiteSpace(current?.StripeCustomerId))
        {
            throw new InvalidOperationException("A paid subscription is required to manage billing.");
        }
        var origin = returnBase.GetLeftPart(UriPartial.Authority);
        var fields = type == "checkout"
            ? new Dictionary<string, string>
            {
                ["mode"] = "subscription",
                ["line_items[0][price]"] = price!,
                ["line_items[0][quantity]"] = "1",
                ["client_reference_id"] = tenant.TenantId.ToString("N"),
                ["metadata[tenant_id]"] = tenant.TenantId.ToString("D"),
                ["subscription_data[metadata][tenant_id]"] = tenant.TenantId.ToString("D"),
                // The portal no longer ships a billing page (open-source, self-hosted product);
                // checkout returns to the workspace root.
                ["success_url"] = origin + "/?checkout=success",
                ["cancel_url"] = origin + "/?checkout=cancel"
            }
            : new Dictionary<string, string>
            {
                ["customer"] = current!.StripeCustomerId!,
                ["return_url"] = origin + "/"
            };
        if (type == "checkout" && !string.IsNullOrEmpty(current?.StripeCustomerId))
        {
            fields["customer"] = current.StripeCustomerId;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post,
            type == "checkout" ? "https://api.stripe.com/v1/checkout/sessions"
                               : "https://api.stripe.com/v1/billing_portal/sessions")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.SecretKey);
        using var client = clients.CreateClient("stripe-billing");
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Stripe billing is unavailable; no plan change was made.");
        }
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("url", out var element)
            || !Uri.TryCreate(element.GetString(), UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || !url.Host.EndsWith(".stripe.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stripe did not return a valid billing URL.");
        }
        return url;
    }
}
