using PlaceContext.Infrastructure.Persistence;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// <see cref="JobRow.ConcurrencyLimit"/>'s default — raised 1 → 4 so multi-shard jobs parallelize their
/// shards by default (see <c>EfJobRunRepository.DeserialiseSnapshot</c>'s unrelated "&lt; 1 ? 1" clamp,
/// which still guards a 0/negative persisted value down to 1, not up to this new default).
/// </summary>
public class JobRowTests
{
    [Fact]
    public void ConcurrencyLimit_DefaultsTo4()
    {
        var row = new JobRow();

        Assert.Equal(4, row.ConcurrencyLimit);
    }
}
