using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Infrastructure.Scheduling;

namespace PlaceContext.Infrastructure.Tests;

public sealed class RpcChainSubmissionBackgroundServiceTests
{
    [Theory]
    [InlineData(null, 2)]
    [InlineData("4", 4)]
    public async Task Refills_free_slots_without_waiting_for_slow_chains(string? configured, int limit)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new ProbeWorker(services, Config(configured));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await worker.StartAsync(timeout.Token);
        try
        {
            for (var i = 0; i < limit; i++)
            {
                await worker.Started.Reader.ReadAsync(timeout.Token);
            }            // Leave all chains blocked across a polling tick: the cap must hold.
            await Task.Delay(TimeSpan.FromSeconds(3.5), timeout.Token);
            Assert.False(worker.Started.Reader.TryRead(out _));
            Assert.Equal(limit, worker.Active);

            worker.ReleaseFirst.SetResult();
            await worker.Started.Reader.ReadAsync(timeout.Token);
            Assert.Equal(limit, worker.Active);
            Assert.Equal(limit + 1, worker.Claimed);
        }
        finally
        {
            await worker.StopAsync(timeout.Token);
        }
        Assert.Equal(0, worker.Active);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Rejects_nonpositive_limits(string configured)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeWorker(services, Config(configured)));
    }

    private static IConfiguration Config(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:Scheduling:MaxConcurrentChains"] = value
        }).Build();

    private sealed class ProbeWorker(IServiceProvider services, IConfiguration configuration)
        : RpcChainSubmissionBackgroundService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RpcChainSubmissionBackgroundService>.Instance, configuration)
    {
        public readonly Channel<int> Started = Channel.CreateUnbounded<int>();
        public readonly TaskCompletionSource ReleaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active;
        public int Claimed;
        private int _started;

        protected override Task<IReadOnlyList<ClaimedSubmission>> ClaimAsync(int available, CancellationToken ct)
        {
            Claimed += available;
            IReadOnlyList<ClaimedSubmission> values = Enumerable.Range(0, available)
                .Select(_ => new ClaimedSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Admin")).ToArray();
            return Task.FromResult(values);
        }

        protected override async Task ProcessAsync(ClaimedSubmission value, CancellationToken ct)
        {
            var index = Interlocked.Increment(ref _started);
            Interlocked.Increment(ref Active);
            Started.Writer.TryWrite(index);
            try
            {
                async Task HandleSubmissionAsync()
                {
                    if (index == 1)
                    {
                        await ReleaseFirst.Task.WaitAsync(ct);
                        return;
                    }
                    await Task.Delay(Timeout.Infinite, ct);
                }
                await HandleSubmissionAsync();
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }

        protected override Task CleanupTrackingAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
