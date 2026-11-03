using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class JobDataAccessTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Only_opted_in_map_or_reduce_receives_transient_token(bool mapAccess, bool reduceAccess)
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var runner = new FakeWorkloadRunner();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var tokens = new Tokens();
        var handler = new RunJobHandler(jobs, runs, runner, new RecordingUnitOfWork(), clock, dataTokens: tokens);
        var mapEnv = new Dictionary<string, string> { ["PC_USE_PROJECT_DATA"] = mapAccess.ToString(), ["PC_DATA_TOKEN"] = "stale" };
        var reduceEnv = new Dictionary<string, string> { ["PC_USE_PROJECT_DATA"] = reduceAccess.ToString(), ["PC_DATA_TOKEN"] = "stale" };
        var job = Job.Create(Guid.NewGuid(), "Data", null,
            new MapSpec("worker", ["{}"], mapEnv), new ReduceSpec("worker", reduceEnv),
            1, ExitCodePolicy.Default, clock.UtcNow, allowNetworkEgress: true);
        await jobs.AddAsync(job);
        var result = await handler.HandleAsync(new RunJobCommand(job.Id));
        Assert.Equal(mapAccess || reduceAccess ? 1 : 0, tokens.Calls);
        Assert.Equal(mapAccess, runner.ReceivedRequests[0].Env.ContainsKey("PC_DATA_TOKEN"));
        Assert.Equal(reduceAccess, runner.ReceivedRequests[1].Env.ContainsKey("PC_DATA_TOKEN"));
        if (mapAccess || reduceAccess)
        {
            Assert.Equal(result.Id, tokens.RunId);
            Assert.Contains(runner.ReceivedRequests, request => request.Env.GetValueOrDefault("PC_DATA_TOKEN") == "issued-credential");
        }
        var persisted = await runs.GetByIdAsync(result.Id);
        Assert.Equal("stale", persisted!.Snapshot.MapEnv["PC_DATA_TOKEN"]);
        Assert.Equal("stale", job.MapSpec.Env["PC_DATA_TOKEN"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Missing_network_or_denied_issuance_prevents_worker_start(bool network, bool deny)
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var runner = new FakeWorkloadRunner();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var tokens = new Tokens { Deny = deny };
        var handler = new RunJobHandler(jobs, runs, runner, new RecordingUnitOfWork(), clock, dataTokens: tokens);
        var job = Job.Create(Guid.NewGuid(), "Data", null,
            new MapSpec("worker", ["{}"], new Dictionary<string, string> { ["PC_USE_PROJECT_DATA"] = "true" }),
            null, 1, ExitCodePolicy.Default, clock.UtcNow, allowNetworkEgress: network);
        await jobs.AddAsync(job);
        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(new RunJobCommand(job.Id)));
        Assert.Empty(runner.ReceivedRequests);
    }

    [Fact]
    public async Task Queries_use_the_authenticated_project_and_fail_without_a_grant()
    {
        var project = Guid.NewGuid();
        var reader = new Reader();
        var handler = new QueryJobDataHandler(new Access(project), reader);
        await handler.HandleAsync(new("SELECT 1"));
        Assert.Equal(project, reader.Last!.ProjectId);
        var denied = new QueryJobDataHandler(new Access(null), reader);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.HandleAsync(new("SELECT 1")));
        Assert.Equal(1, reader.Calls);
    }

    private sealed class Access(Guid? project) : IJobDataAccess
    {
        public Guid RequireProjectId() => project ?? throw new UnauthorizedAccessException();
    }

    private sealed class Reader : IQueryHandler<QueryProjectDataQuery, ProjectQueryResult>
    {
        public QueryProjectDataQuery? Last;
        public int Calls;
        public Task<ProjectQueryResult> HandleAsync(QueryProjectDataQuery query, CancellationToken ct = default)
        {
            Last = query;
            Calls++;
            return Task.FromResult(new ProjectQueryResult(["value"], [["1"]], 0, false));
        }
    }

    private sealed class Tokens : IJobDataTokenService
    {
        public int Calls;
        public Guid RunId;
        public bool Deny;
        public Task<string> CreateAsync(Guid runId, CancellationToken ct = default)
        {
            Calls++;
            RunId = runId;
            if (Deny)
            {
                throw new UnauthorizedAccessException();
            }
            return Task.FromResult("issued-credential");
        }
        public Task<JobDataGrant?> ValidateAsync(string token, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
