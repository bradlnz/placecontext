using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class CrmChainRunTests
{
    [Fact]
    public void Create_links_a_chain_run_to_the_client_lifecycle()
    {
        var chainRunId = Guid.NewGuid();
        var value = CrmChainRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            chainRunId,
            CustomerLifecycleStage.Onboarding,
            DateTimeOffset.UtcNow);

        Assert.Equal(chainRunId, value.ChainRunId);
        Assert.Equal(CustomerLifecycleStage.Onboarding, value.LifecycleStage);
    }

    [Fact]
    public void Create_rejects_empty_identifiers()
        => Assert.Throws<ArgumentException>(() => CrmChainRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            CustomerLifecycleStage.Active,
            DateTimeOffset.UtcNow));
}
