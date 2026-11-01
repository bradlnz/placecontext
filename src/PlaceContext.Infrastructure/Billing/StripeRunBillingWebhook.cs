using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Billing;

/// <summary>Signed Stripe event processor. An untrusted browser cannot mutate plan entitlements.
/// The event ID is unique and subscription event timestamps prevent older webhook deliveries
/// from rolling back newer entitlement state.</summary>
public sealed class StripeRunBillingWebhook(AppDbContext db, IOptions<StripeRunBillingOptions> settings)
{
    public static bool Verify(string body, string header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || body.Length > 1024 * 1024) return false;
        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0] == "t") timestamp = pair[1];
            if (pair[0] == "v1") signatures.Add(pair[1]);
        }
        if (timestamp is null || !long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)
            || signatures.Count == 0) return false;
        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(epoch); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (Math.Abs((now - signedAt).TotalSeconds) > 300) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(timestamp + "." + body));
        foreach (var value in signatures)
        {
            try
            {
                if (CryptographicOperations.FixedTimeEquals(Convert.FromHexString(value), expected))
                    return true;
            }
            catch (FormatException) { }
        }
        return false;
    }

    public async Task ApplyAsync(string body, string signature, CancellationToken ct = default)
    {
        if (!Verify(body, signature, settings.Value.WebhookSecret, DateTimeOffset.UtcNow))
            throw new UnauthorizedAccessException("Invalid billing webhook signature.");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString();
        var type = root.GetProperty("type").GetString();
        var created = root.GetProperty("created").GetInt64();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new InvalidOperationException("Invalid event id");
        if (type is not ("checkout.session.completed" or "customer.subscription.created"
            or "customer.subscription.updated" or "customer.subscription.deleted"))
            return;
        var obj = root.GetProperty("data").GetProperty("object");
        Guid tenantId;
        var metadata = obj.TryGetProperty("metadata", out var m) ? m : default;
        var tenantText = TryString(metadata, "tenant_id");
        if (type == "checkout.session.completed")
            tenantText ??= TryString(obj, "client_reference_id");
        if (!Guid.TryParse(tenantText, out tenantId) || tenantId == Guid.Empty)
            throw new InvalidOperationException("Billing event has no trusted tenant association.");
        var customer = TryString(obj, "customer");
        if (string.IsNullOrEmpty(customer) || customer.Length > 200)
            throw new InvalidOperationException("Billing event has no customer ID.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw new InvalidOperationException("Unknown billing tenant.");
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO stripe_billing_events ("EventId", "ProcessedAt")
            VALUES ({id}, {DateTimeOffset.UtcNow}) ON CONFLICT ("EventId") DO NOTHING
            """, ct);
        if (claimed == 0) { await tx.CommitAsync(ct); return; }
        if (type == "checkout.session.completed")
        {
            // Checkout only associates the Stripe Customer. A redirect or a completed
            // session is not proof that the subscription is active/paid.
            var period = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenant_run_plans ("TenantId", "Plan", "Status", "IncludedRuns",
                    "UsedRuns", "PeriodStart", "PeriodEnd", "StripeCustomerId", "LastStripeEventCreated")
                VALUES ({tenantId}, {"free"}, {"active"}, {RunPlans.FreeRuns}, {0}, {period},
                    {period.AddMonths(1)}, {customer}, {0L})
                ON CONFLICT ("TenantId") DO UPDATE SET
                  "StripeCustomerId" = EXCLUDED."StripeCustomerId"
                WHERE tenant_run_plans."StripeCustomerId" IS NULL
                   OR tenant_run_plans."StripeCustomerId" = EXCLUDED."StripeCustomerId"
                """, ct);
            await tx.CommitAsync(ct);
            return;
        }
        var subscription = TryString(obj, "id");
        var status = TryString(obj, "status") ?? "inactive";
        // Match the exact subscription item and its period to an operator-configured
        // entitlement. Enterprise prices and run quotas are bound to a specific tenant.
        var matches = obj.GetProperty("items").GetProperty("data").EnumerateArray()
            .Select(item => (Item: item, Price: TryString(item.GetProperty("price"), "id")))
            .Where(item => settings.Value.EntitlementForPrice(item.Price, tenantId) is not null)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("A subscription must contain exactly one recognised run plan price.");
        var matchedItem = matches[0].Item;
        if (matchedItem.TryGetProperty("quantity", out var quantity)
            && (!quantity.TryGetInt32(out var units) || units != 1))
            throw new InvalidOperationException("Run plan subscriptions must have quantity one.");
        var entitlement = settings.Value.EntitlementForPrice(matches[0].Price, tenantId)!;
        var plan = entitlement.Plan;
        var includedRuns = entitlement.IncludedRuns;
        var start = Unix(matchedItem, "current_period_start")
            ?? Unix(obj, "current_period_start")
            ?? throw new InvalidOperationException("Subscription period start missing.");
        var end = Unix(matchedItem, "current_period_end")
            ?? Unix(obj, "current_period_end")
            ?? throw new InvalidOperationException("Subscription period end missing.");
        if (start >= end || end - start > TimeSpan.FromDays(400))
            throw new InvalidOperationException("Invalid subscription period.");
        if (string.IsNullOrEmpty(subscription)) throw new InvalidOperationException("Subscription id missing.");
        var entitlementStatus = status is "active" or "trialing" ? status : "inactive";

        // Signed event, known price ID, verified tenant ID, monotonic timestamp; no
        // self-service setting endpoint can increase IncludedRuns.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tenant_run_plans ("TenantId", "Plan", "Status", "IncludedRuns",
                "UsedRuns", "PeriodStart", "PeriodEnd", "StripeCustomerId",
                "StripeSubscriptionId", "LastStripeEventCreated")
            VALUES ({tenantId}, {plan}, {entitlementStatus}, {includedRuns}, {0},
                {start}, {end}, {customer}, {subscription}, {created})
            ON CONFLICT ("TenantId") DO UPDATE SET
                "Plan" = EXCLUDED."Plan",
                "Status" = EXCLUDED."Status",
                "IncludedRuns" = EXCLUDED."IncludedRuns",
                "UsedRuns" = CASE WHEN tenant_run_plans."PeriodStart" = EXCLUDED."PeriodStart"
                    THEN tenant_run_plans."UsedRuns" ELSE 0 END,
                "PeriodStart" = EXCLUDED."PeriodStart",
                "PeriodEnd" = EXCLUDED."PeriodEnd",
                "StripeCustomerId" = EXCLUDED."StripeCustomerId",
                "StripeSubscriptionId" = EXCLUDED."StripeSubscriptionId",
                "LastStripeEventCreated" = EXCLUDED."LastStripeEventCreated"
            WHERE tenant_run_plans."LastStripeEventCreated" <= EXCLUDED."LastStripeEventCreated"
              AND (tenant_run_plans."StripeCustomerId" IS NULL
                   OR tenant_run_plans."StripeCustomerId" = EXCLUDED."StripeCustomerId")
              -- Do not let a late cancellation for an older subscription disable a
              -- newer active subscription belonging to the same Stripe customer.
              AND (EXCLUDED."Status" <> 'inactive'
                   OR tenant_run_plans."StripeSubscriptionId" IS NULL
                   OR tenant_run_plans."StripeSubscriptionId" = EXCLUDED."StripeSubscriptionId")
            """, ct);
        await tx.CommitAsync(ct);
    }

    private static string? TryString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static DateTimeOffset? Unix(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var item) || !item.TryGetInt64(out var seconds))
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
