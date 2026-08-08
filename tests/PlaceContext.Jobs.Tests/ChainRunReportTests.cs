using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The cross-project chain history feed (ListRecentChainRuns): recent chain runs across every chain
/// and project, newest first — Observability's "Chains" tab, mirroring ListRecentRunReports for jobs.
/// </summary>
public class ChainRunReportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static JobChain MakeChain(string name, Guid projectId)
        => JobChain.Create(projectId, name, null, new[] { Guid.NewGuid() }, T0);

    [Fact]
    public async Task Recent_chain_runs_come_back_newest_first_with_their_project_names()
    {
        var runs = new InMemoryChainRunRepository();
        var older = MakeChain("older-chain", Guid.NewGuid());
        var newer = MakeChain("newer-chain", Guid.NewGuid());
        await runs.AddAsync(ChainRun.Start(older, new[] { "step" }, T0));
        await runs.AddAsync(ChainRun.Start(newer, new[] { "step" }, T0.AddMinutes(5)));

        var handler = new ListRecentChainRunsHandler(runs, new InMemoryProjectRepository());
        var reports = await handler.HandleAsync(new ListRecentChainRunsQuery(Take: 10));

        Assert.Equal(2, reports.Count);
        Assert.Equal("newer-chain", reports[0].Run.ChainName);
        Assert.Equal("older-chain", reports[1].Run.ChainName);
        Assert.Equal(newer.ProjectId, reports[0].ProjectId);
        Assert.Equal("(deleted project)", reports[0].ProjectName); // no project rows registered here
    }

    [Fact]
    public async Task Take_caps_the_cross_project_feed()
    {
        var runs = new InMemoryChainRunRepository();
        var chain = MakeChain("kept-chain", Guid.NewGuid());
        for (var i = 0; i < 5; i++)
            await runs.AddAsync(ChainRun.Start(chain, new[] { "step" }, T0.AddMinutes(i)));

        var handler = new ListRecentChainRunsHandler(runs, new InMemoryProjectRepository());
        var reports = await handler.HandleAsync(new ListRecentChainRunsQuery(Take: 3));

        Assert.Equal(3, reports.Count);
    }
}
