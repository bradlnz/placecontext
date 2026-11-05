using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class AgentActivityPersistenceTests
{
    [Fact]
    public async Task Activity_survives_reload_and_repeat_and_old_steps_still_load()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, tenant);
        var repository = new EfChainRunRepository(db, new FakeDataEncryptor());
        var now = DateTimeOffset.UtcNow;
        var chain = JobChain.Create(Guid.NewGuid(), "agent", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "result")),
             ChainStage.ForAction(new RepeatFlowChainAction("eq:ready:true"))], now);
        var run = ChainRun.Start(chain, ["Agent task", "Repeat"], now);
        var jobId = Guid.NewGuid();
        var jobRunId = Guid.NewGuid();
        var entry = new AgentActivity(now, "tool_completed", "Tool completed", 1, "run_job", jobId, jobRunId, 2.5);
        run.AppendAgentActivity(0, entry);
        run.RepeatFlow(1, "{}", now);
        await repository.AddAsync(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = (await repository.GetByIdAsync(run.Id))!;
        Assert.Equal(entry, Assert.Single(loaded.Steps[0].AgentActivity!));
        Assert.Null(loaded.Steps[1].AgentActivity);
        await using var other = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Null(await new EfChainRunRepository(other, new FakeDataEncryptor()).GetByIdAsync(run.Id));

        // Legacy rows have no activity property at all.
        var row = await db.ChainRuns.SingleAsync();
        var json = System.Text.Json.Nodes.JsonNode.Parse(row.StepsJson)!.AsArray();
        foreach (var step in json) step!.AsObject().Remove("agentActivity");
        row.StepsJson = json.ToJsonString();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.All((await repository.GetByIdAsync(run.Id))!.Steps, step => Assert.Null(step.AgentActivity));
    }

    [Fact]
    public void Activity_keeps_recent_events_and_bounds_message_size()
    {
        var now = DateTimeOffset.UtcNow;
        var chain = JobChain.Create(Guid.NewGuid(), "agent", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "result"))], now);
        var run = ChainRun.Start(chain, ["Agent task"], now);
        for (var i = 0; i <= AgentActivity.RetainedEvents; i++)
            run.AppendAgentActivity(0, new(now, "update", new string('x', 2100), Turn: i));
        Assert.Equal(AgentActivity.RetainedEvents, run.Steps[0].AgentActivity!.Count);
        Assert.Equal(1, run.Steps[0].AgentActivity![0].Turn);
        Assert.All(run.Steps[0].AgentActivity!, item => Assert.Equal(2001, item.Message.Length));
    }
}
