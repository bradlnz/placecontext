using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

/// <summary>Signed provider lifecycle against a real PostgreSQL schema. Local test data
/// simulates Stripe events; live provider delivery still requires configured credentials.</summary>
public sealed class StripeBillingWebhookPostgresTests
{
    [AgentPostgresFact]
    public async Task Signed_subscription_grants_unlimited_runs_and_duplicates_cannot_reapply()
    {
        var schema = "stripe_evt_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var setup = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn))
            await setup.ExecuteNonQueryAsync();
        try
        {
            var id = Guid.NewGuid();
            var owner = new FakeCurrentTenant(id);
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            await using var db = new AppDbContext(dbOptions, owner);
            await db.Database.MigrateAsync();
            db.Tenants.Add(new TenantRow { Id = id, Slug = schema, Name = schema,
                CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            var options = Options.Create(new StripeRunBillingOptions
            {
                WebhookSecret = "whsec_test_saas",
                BasicPriceId = "price_basic",
                StandardPriceId = "price_standard"
            });
            var webhook = new StripeRunBillingWebhook(db, options);
            var now = DateTimeOffset.UtcNow;
            string Event(string eventId, string status, string price, string subscription)
                => JsonSerializer.Serialize(new
                {
                    id = eventId,
                    type = "customer.subscription.updated",
                    created = now.ToUnixTimeSeconds(),
                    data = new
                    {
                        @object = new
                        {
                            id = subscription,
                            customer = "cus_test",
                            status,
                            metadata = new { tenant_id = id.ToString("D") },
                            items = new
                            {
                                data = new[] { new
                                {
                                    price = new { id = price }, quantity = 1,
                                    current_period_start = now.AddDays(-2).ToUnixTimeSeconds(),
                                    current_period_end = now.AddDays(28).ToUnixTimeSeconds()
                                } }
                            }
                        }
                    }
                });
            string SignedHeader(string body)
            {
                var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.WebhookSecret),
                    Encoding.UTF8.GetBytes(stamp + "." + body));
                return $"t={stamp},v1={Convert.ToHexString(digest).ToLowerInvariant()}";
            }

            var basic = Event("evt_basic", "active", "price_basic", "sub_one");
            await webhook.ApplyAsync(basic, SignedHeader(basic));
            await webhook.ApplyAsync(basic, SignedHeader(basic));
            var state = await db.TenantRunPlans.AsNoTracking().SingleAsync();
            Assert.Equal(RunPlans.Basic, state.Plan);
            Assert.Equal(0, state.IncludedRuns);
            Assert.Equal("active", state.Status);
            Assert.Equal(1, await db.StripeBillingEvents.CountAsync());

            var unknown = Event("evt_unknown", "active", "price_untrusted", "sub_one");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                webhook.ApplyAsync(unknown, SignedHeader(unknown)));
            // A rejected event is rolled back, not an entitlement transition.
            Assert.Equal("active", (await db.TenantRunPlans.AsNoTracking().SingleAsync()).Status);

            var cancelled = Event("evt_cancel", "canceled", "price_basic", "sub_one");
            await webhook.ApplyAsync(cancelled, SignedHeader(cancelled));
            Assert.Equal("inactive", (await db.TenantRunPlans.AsNoTracking().SingleAsync()).Status);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
