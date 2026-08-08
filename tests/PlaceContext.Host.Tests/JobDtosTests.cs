using PlaceContext.Jobs.Contracts.Management;
using Xunit;

namespace PlaceContext.Host.Tests;

/// <summary>
/// <see cref="JobRequest"/>'s default <c>ConcurrencyLimit</c> — the management API contract new/PUT
/// callers get when they don't specify it. Raised 1 → 4 (see <c>JobRow.ConcurrencyLimit</c>'s matching
/// default in Infrastructure) so multi-shard jobs parallelize their shards out of the box; a caller
/// that needs strict serial shard execution still passes 1 explicitly.
/// </summary>
public class JobDtosTests
{
    [Fact]
    public void JobRequest_DefaultConcurrencyLimit_Is4()
    {
        var request = new JobRequest(
            Name: "job",
            Description: null,
            MapImage: "alpine",
            MapRuntimeId: null,
            MapSource: null,
            MapEntrypoint: null,
            MapFiles: null,
            InputPayloads: new[] { "payload" },
            MapEnv: null,
            ReduceImage: null,
            ReduceRuntimeId: null,
            ReduceSource: null,
            ReduceEntrypoint: null,
            ReduceFiles: null,
            ReduceEnv: null
        );

        Assert.Equal(4, request.ConcurrencyLimit);
    }

    [Fact]
    public void JobRequest_ExplicitConcurrencyLimit_IsNotOverridden()
    {
        var request = new JobRequest(
            Name: "job",
            Description: null,
            MapImage: "alpine",
            MapRuntimeId: null,
            MapSource: null,
            MapEntrypoint: null,
            MapFiles: null,
            InputPayloads: new[] { "payload" },
            MapEnv: null,
            ReduceImage: null,
            ReduceRuntimeId: null,
            ReduceSource: null,
            ReduceEntrypoint: null,
            ReduceFiles: null,
            ReduceEnv: null,
            ConcurrencyLimit: 1
        );

        Assert.Equal(1, request.ConcurrencyLimit);
    }
}
