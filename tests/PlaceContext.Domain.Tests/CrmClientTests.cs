using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class CrmClientTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_contact_details_and_starts_in_requested_stage()
    {
        var client = CrmClient.Create(
            Guid.NewGuid(), "  Ada Lovelace  ", "  Analytical Engines  ",
            "  ada@example.test ", null, CustomerLifecycleStage.Qualified, "  Warm lead  ", T0);

        Assert.Equal("Ada Lovelace", client.Name);
        Assert.Equal("Analytical Engines", client.Company);
        Assert.Equal("ada@example.test", client.Email);
        Assert.Equal(CustomerLifecycleStage.Qualified, client.LifecycleStage);
        Assert.Equal(T0, client.UpdatedAt);
    }

    [Fact]
    public void MoveTo_records_the_new_lifecycle_stage_and_update_time()
    {
        var client = CrmClient.Create(
            Guid.NewGuid(), "Ada", null, null, null, CustomerLifecycleStage.Lead, null, T0);
        var movedAt = T0.AddDays(2);

        client.MoveTo(CustomerLifecycleStage.Onboarding, movedAt);

        Assert.Equal(CustomerLifecycleStage.Onboarding, client.LifecycleStage);
        Assert.Equal(movedAt, client.UpdatedAt);
    }

    [Fact]
    public void Create_rejects_invalid_email()
        => Assert.Throws<ArgumentException>(() => CrmClient.Create(
            Guid.NewGuid(), "Ada", null, "not-an-email", null,
            CustomerLifecycleStage.Lead, null, T0));
}
