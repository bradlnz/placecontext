using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class JobChainActionPersistenceTests
{
    [Fact]
    public async Task Qemu_action_round_trips_through_stages_json()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var loginId = Guid.NewGuid();
        var chain = JobChain.Create(Guid.NewGuid(), "vm", null,
            [ChainStage.ForAction(new QemuChainAction("browser", "cat", "result", 2048, 2, 60, true, true, loginId))], T0);
        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = Assert.IsType<QemuChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[0].Action);
        Assert.Equal("browser", loaded.ImageProfile);
        Assert.Equal("cat", loaded.Command);
        Assert.Equal("result", loaded.OutputField);
        Assert.Equal(2048, loaded.MemoryMb);
        Assert.Equal(2, loaded.Cpus);
        Assert.Equal(60, loaded.TimeoutSeconds);
        Assert.True(loaded.AllowNetwork);
        Assert.True(loaded.PauseForLogin);
        Assert.Equal(loginId, loaded.LoginProfileId);
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.TextResponse, LoopChainAction.ExternalAgent)]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.YesNoResponse, LoopChainAction.ExternalAgent)]
    [InlineData(LoopChainAction.AgentType, LoopChainAction.TextResponse, LoopChainAction.ExternalAgent)]
    [InlineData(LoopChainAction.AgentType, LoopChainAction.TextResponse, LoopChainAction.JevAgent)]
    public async Task Loop_action_round_trips_through_stages_json(string kind, string responseType, string agentProvider)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var chain = JobChain.Create(Guid.NewGuid(), "review", null,
            [ChainStage.ForAction(new LoopChainAction(kind, "Review this draft", "review", responseType, agentProvider))], T0);
        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await repository.GetByIdAsync(chain.Id);
        var loop = Assert.IsType<LoopChainAction>(Assert.Single(loaded!.Stages).Action);
        Assert.Equal(kind, loop.Type);
        Assert.Equal(responseType, loop.ResponseType);
        Assert.Equal(agentProvider, loop.AgentProvider);
        Assert.Equal("Review this draft", loop.Instructions);
        Assert.Equal("review", loop.ResponseField);
    }

    [Fact]
    public async Task Typed_email_action_round_trips_through_stages_json()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        var repository = new EfJobChainRepository(db);
        var email = new SendEmailChainAction(
            "{{client.email}}", "{{client.name}}", "Report {{report.id}}", "Ready",
            "{{attachments}}");
        var chain = JobChain.Create(Guid.NewGuid(), "deliver", null,
            new[] { ChainStage.ForAction(email) }, T0);

        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await repository.GetByIdAsync(chain.Id);
        var action = Assert.IsType<SendEmailChainAction>(Assert.Single(loaded!.Stages).Action);
        Assert.Equal("{{client.email}}", action.Recipient);
        Assert.Equal("Report {{report.id}}", action.Subject);
        Assert.Equal("{{attachments}}", action.AttachmentPath);
    }

    [Fact]
    public async Task Legacy_agent_step_without_provider_still_waits_for_external_input()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var row = new JobChainRow { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "Legacy review",
            StagesJson = """[{"jobs":[],"action":{"type":"agentInput","instructions":"Review","responseField":"answer"}}]""",
            CreatedAt = T0, UpdatedAt = T0 };
        db.JobChains.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await new EfJobChainRepository(db).GetByIdAsync(row.Id);
        Assert.Equal(LoopChainAction.ExternalAgent, Assert.IsType<LoopChainAction>(loaded!.Stages[0].Action).AgentProvider);
    }

    [Fact]
    public async Task Legacy_flat_job_id_array_still_deserializes()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        var jobId = Guid.NewGuid();
        var row = new JobChainRow
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "legacy",
            StagesJson = $"[\"{jobId}\"]",
            CreatedAt = T0,
            UpdatedAt = T0,
        };
        await db.JobChains.AddAsync(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await new EfJobChainRepository(db).GetByIdAsync(row.Id);

        Assert.Equal(jobId, Assert.Single(Assert.Single(loaded!.Stages).JobIds));
        Assert.Null(loaded.Stages[0].Action);
    }
}
