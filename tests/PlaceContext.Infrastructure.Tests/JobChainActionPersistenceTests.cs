using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Application.Secrets;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class JobChainActionPersistenceTests
{
    [Fact]
    public async Task Workflow_schedule_target_and_owner_survive_reload()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobTriggerRepository(db);
        var workflow = Guid.NewGuid(); var owner = Guid.NewGuid();
        var schedule = JobTrigger.CreateWorkflowSchedule(Guid.NewGuid(), workflow, owner, "Daily", "0 9 * * *", T0.AddDays(1), T0);
        await repository.AddAsync(schedule); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = (await repository.GetByIdAsync(schedule.Id))!;
        Assert.Null(loaded.JobId); Assert.Equal(workflow, loaded.WorkflowId); Assert.Equal(owner, loaded.ScheduledBy);
        Assert.Equal(T0.AddDays(1), loaded.NextRunAt);
    }

    [Fact]
    public async Task Parallel_workflow_conditions_round_trip()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var branches = new[] { new ConditionalWorkflowBranch(Guid.NewGuid(), "Writer", "eq:ready:true"), new ConditionalWorkflowBranch(Guid.NewGuid(), "Planner", "gte:score:0.9") };
        var chain = JobChain.Create(Guid.NewGuid(), "Parent", null, [ChainStage.ForAction(new ParallelWorkflowsChainAction(branches, "joined"))], T0);
        await repository.AddAsync(chain); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = Assert.IsType<ParallelWorkflowsChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[0].Action);
        Assert.Equal(branches, loaded.Branches); Assert.Equal("joined", loaded.OutputField);
    }

    [Fact]
    public async Task Sub_workflow_and_compound_gate_round_trip()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var childId = Guid.NewGuid();
        const string expression = "all:[\"gte:score:0.9\",\"eq:approved:true\"]";
        var chain = JobChain.Create(Guid.NewGuid(), "Parent", null,
            [new ChainStage([], new ConditionGate(expression), action: new SubWorkflowChainAction(childId, "Review"))], T0);
        await repository.AddAsync(chain); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = (await repository.GetByIdAsync(chain.Id))!.Stages[0];
        Assert.Equal(childId, Assert.IsType<SubWorkflowChainAction>(loaded.Action).WorkflowId);
        Assert.Equal(expression, Assert.IsType<ConditionGate>(loaded.Gate).Expression);
    }

    [Fact]
    public async Task Container_steps_preserve_build_context_and_run_limits()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var chain = JobChain.Create(Guid.NewGuid(), "containers", null,
            [ChainStage.ForAction(new ContainerBuildChainAction("FROM alpine:3.21", "source.files", "built", 120, true)),
             ChainStage.ForAction(new ContainerRunChainAction(imageField: "built.image", command: ["sleep", "60"], port: 8080, memoryMb: 256, cpus: 2))], T0);
        await repository.AddAsync(chain); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var stages = (await repository.GetByIdAsync(chain.Id))!.Stages;
        var build = Assert.IsType<ContainerBuildChainAction>(stages[0].Action);
        var run = Assert.IsType<ContainerRunChainAction>(stages[1].Action);
        Assert.Equal("source.files", build.ContextField); Assert.True(build.AllowNetwork); Assert.Equal(120, build.TimeoutSeconds);
        Assert.Equal("built.image", run.ImageField); Assert.Equal(["sleep", "60"], run.Command); Assert.Equal(8080, run.Port);
        Assert.Equal(256, run.MemoryMb); Assert.Equal(2, run.Cpus); Assert.False(run.AllowNetwork);
    }

    [Fact]
    public async Task Step_output_is_encrypted_round_trips_and_old_runs_without_output_still_load()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
        var repository = new EfChainRunRepository(db, encryptor);
        var chain = JobChain.Create(Guid.NewGuid(), "review", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Review", "result"))], T0);
        var run = ChainRun.Start(chain, ["Review"], T0);
        run.MarkStepRunning(0, null, T0);
        run.MarkStepFinished(0, null, ChainStepStatus.Succeeded, T0, output: "{\"private_step_result\":42}");
        await repository.AddAsync(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var row = await db.ChainRuns.SingleAsync();
        Assert.DoesNotContain("private_step_result", row.StepsJson);
        Assert.Equal(run.Steps[0].Output, (await repository.GetByIdAsync(run.Id))!.Steps[0].Output);
        var steps = System.Text.Json.Nodes.JsonNode.Parse(encryptor.Unprotect(row.StepsJson, IDataEncryptor.Purpose.ChainRun))!;
        steps[0]!.AsObject().Remove("output");
        row.StepsJson = encryptor.Protect(steps.ToJsonString(), IDataEncryptor.Purpose.ChainRun);
        await db.SaveChangesAsync();
        Assert.Null((await repository.GetByIdAsync(run.Id))!.Steps[0].Output);
    }

    [Fact]
    public async Task Query_data_preserves_SQL_and_output_field()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var chain = JobChain.Create(Guid.NewGuid(), "query", null, [ChainStage.ForAction(new QueryDataChainAction("SELECT id FROM listings LIMIT 10", "listings"))], T0);
        await repository.AddAsync(chain); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = Assert.IsType<QueryDataChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[0].Action);
        Assert.Equal("SELECT id FROM listings LIMIT 10", loaded.Sql); Assert.Equal("listings", loaded.OutputField);
    }

    [Fact]
    public async Task Scoring_gate_preserves_rubric_thresholds_and_failure_policy()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var action = new ScoringGateChainAction("review.security", "gate", [new ScoreCriterion("security", "Safe?", ["Fail", "Pass"], 50, 90)], 92, .85, false);
        var chain = JobChain.Create(Guid.NewGuid(), "scoring", null, [ChainStage.ForAction(action)], T0);
        await repository.AddAsync(chain); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = Assert.IsType<ScoringGateChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[0].Action);
        Assert.Equal("review.security", loaded.InputField); Assert.Equal(92, loaded.PassScore); Assert.Equal(.85, loaded.MinimumConfidence);
        Assert.False(loaded.StopOnFailure); Assert.Equal(90, loaded.Criteria[0].MinimumScore); Assert.Equal(["Fail", "Pass"], loaded.Criteria[0].Levels);
    }

    [Fact]
    public async Task Automatic_agent_model_selection_round_trips()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var connection = Guid.NewGuid();
        var agent = Guid.NewGuid();
        var chain = JobChain.Create(Guid.NewGuid(), "agent", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "result",
                agentProvider: LoopChainAction.AutomaticAgent, connectionId: connection, model: "test-model", agentId: agent))], T0);
        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = Assert.IsType<LoopChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[0].Action);
        Assert.Equal(LoopChainAction.AutomaticAgent, loaded.AgentProvider);
        Assert.Equal(connection, loaded.ConnectionId);
        Assert.Equal("test-model", loaded.Model);
        Assert.Equal(agent, loaded.AgentId);
    }

    [Fact]
    public async Task Qemu_action_round_trips_through_stages_json()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var loginId = Guid.NewGuid();
        var chain = JobChain.Create(Guid.NewGuid(), "vm", null,
            [ChainStage.ForAction(new VirtualMachineChainAction("browser", "cat", "result", 2048, 2, 60, true, true, loginId))], T0);
        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = Assert.IsType<VirtualMachineChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[0].Action);
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

    [Fact]
    public async Task Repeat_action_round_trips_through_stages_json()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfJobChainRepository(db);
        var chain = JobChain.Create(Guid.NewGuid(), "repeat", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "build")),
             ChainStage.ForAction(new RepeatFlowChainAction("eq:ready:true", 3))], T0);
        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = Assert.IsType<RepeatFlowChainAction>((await repository.GetByIdAsync(chain.Id))!.Stages[1].Action);
        Assert.Equal("eq:ready:true", loaded.UntilCondition);
        Assert.Equal(3, loaded.MaxAttempts);
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.TextResponse, LoopChainAction.ExternalAgent)]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.YesNoResponse, LoopChainAction.ExternalAgent)]
    [InlineData(LoopChainAction.AgentType, LoopChainAction.TextResponse, LoopChainAction.ExternalAgent)]
    [InlineData(LoopChainAction.TaskType, LoopChainAction.TextResponse, LoopChainAction.ExternalAgent)]
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
        var row = new JobChainRow
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Legacy review",
            StagesJson = """[{"jobs":[],"action":{"type":"agentInput","instructions":"Review","responseField":"answer"}}]""",
            CreatedAt = T0,
            UpdatedAt = T0
        };
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
