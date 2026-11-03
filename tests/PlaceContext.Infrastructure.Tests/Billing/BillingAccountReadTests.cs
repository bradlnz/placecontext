using Microsoft.EntityFrameworkCore;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class BillingAccountReadTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("cus_existing", true)]
    public async Task Billing_account_availability_comes_from_the_current_tenants_customer_record(
        string? customerId, bool expected)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        await using var db = new AppDbContext(options, tenant);
        db.TenantRunPlans.Add(new TenantRunPlanRow
        {
            TenantId = tenant.TenantId,
            StripeCustomerId = customerId,
            Status = "canceled",
            PeriodStart = clock.UtcNow.AddDays(-1),
            PeriodEnd = clock.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var snapshot = await new DbRunAllowance(db, tenant, clock).GetAsync();
        Assert.Equal(expected, snapshot.HasBillingAccount);
        Assert.False(snapshot.CanRun);

        var otherTenant = new FakeCurrentTenant(Guid.NewGuid());
        await using var otherDb = new AppDbContext(options, otherTenant);
        var otherSnapshot = await new DbRunAllowance(otherDb, otherTenant, clock).GetAsync();
        Assert.False(otherSnapshot.HasBillingAccount);
    }
}
