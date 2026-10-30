using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public sealed class JobChainLookupTests
{
    [Fact]
    public async Task Listing_chains_batches_names_once_including_nested_branches_and_preserves_repeated_steps()
    {
        var project = Guid.NewGuid();
        var known = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var jobs = new NamesOnlyJobs(known);
        var chains = new InMemoryJobChainRepository();
        for (var i = 0; i < 100; i++)
            await chains.AddAsync(JobChain.Create(project, "Workflow " + i, null,
                [new ChainStage([known, known], new ConditionGate("true", [ChainStage.Of(missing)]))], DateTimeOffset.UtcNow));

        var views = await new ListJobChainsHandler(chains, jobs).HandleAsync(new(project));

        Assert.Equal(100, views.Count);
        Assert.Equal(1, jobs.Calls);
        Assert.Equal(2, jobs.Ids.Count);
        Assert.All(views, view =>
        {
            var stage = Assert.Single(view.Stages);
            Assert.Equal(2, stage.Jobs.Count);
            Assert.All(stage.Jobs, step => Assert.Equal("Job", step.JobName));
            Assert.Equal("(deleted)", Assert.Single(Assert.Single(stage.ElseBranch!).Jobs).JobName);
        });
    }

    private sealed class NamesOnlyJobs(Guid id) : IJobRepository
    {
        public int Calls;
        public IReadOnlyCollection<Guid> Ids = [];
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            Calls++;
            Ids = ids;
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [id] = "Job" });
        }
        public Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new InvalidOperationException("Do not load job source for a list.");
        public Task<IReadOnlyList<Job>> ListForProjectAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Job job, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
