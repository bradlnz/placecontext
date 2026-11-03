using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PlaceContext.Application.Access;
using PlaceContext.Application.AgentWorkflows;
namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

public sealed class AgentWorkflowServiceTests
{
    private const string Input = """{"client":"A customer","scope":"Onboarding","documents":"A signed scope"}""";

    [Fact]
    public async Task Free_checklists_need_no_subscription_or_model_and_catalog_exposes_only_process()
    {
        await using var f = new AgentWorkflowFixture();
        await f.SeedAsync();
        var service = f.Service();
        var workspace = await service.LoadAsync(f.ProjectId);
        Assert.Equal(6, workspace.Workflows.Count);
        Assert.Equal(3, workspace.Workflows.Count(x => !x.Paid));
        Assert.DoesNotContain("Instruction", JsonSerializer.Serialize(workspace));
        var result = await service.RunAsync(f.ProjectId, "services-checklist", Guid.NewGuid(), Input);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(0m, result.DemoCredits);
        Assert.Equal(0, f.Model.Calls);
        var missing = await service.RunAsync(f.ProjectId, "services-checklist", Guid.NewGuid(), "{}");
        Assert.Equal(AgentRunStatus.NeedsReview, missing.Status);
        Assert.Contains("client", missing.Output);
    }

    [Fact]
    public async Task Paid_access_permissions_and_production_boundaries_are_enforced_on_the_server()
    {
        await using var f = new AgentWorkflowFixture();
        await f.SeedAsync();
        var service = f.Service();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync(f.ProjectId, "services", Guid.NewGuid(), Input));
        f.Permissions.Denied.Add(Permission.SettingsManage);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetSubscriptionAsync(f.ProjectId, "services", true));
        f.Permissions.Denied.Clear();
        await service.SetSubscriptionAsync(f.ProjectId, "services", true);
        f.Permissions.Denied.Add(Permission.JobsRun);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync(f.ProjectId, "services", Guid.NewGuid(), Input));
        f.Permissions.Denied.Clear();
        f.Environment.EnvironmentName = Environments.Production;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetSubscriptionAsync(f.ProjectId, "services", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync(f.ProjectId, "services", Guid.NewGuid(), Input));
        Assert.False((await service.LoadAsync(f.ProjectId)).SimulationEnabled);
        Assert.Equal(0, f.Model.Calls);
    }

    [Fact]
    public async Task Subscription_cancellation_blocks_new_runs_but_replay_is_idempotent_and_usage_is_persisted_once()
    {
        await using var f = new AgentWorkflowFixture();
        await f.SeedAsync();
        var service = f.Service();
        await service.SetSubscriptionAsync(f.ProjectId, "services", true);
        var request = Guid.NewGuid();
        var result = await service.RunAsync(f.ProjectId, "services", request, Input);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(1.25m, result.DemoCredits);
        await service.SetSubscriptionAsync(f.ProjectId, "services", false);
        Assert.Equal(result, await service.RunAsync(f.ProjectId, "services", request, Input));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync(f.ProjectId, "services", Guid.NewGuid(), Input));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(f.ProjectId, "services", request, "{}"));
        await using var db = f.Db();
        Assert.Single(await db.UsageRecords.ToListAsync());
        Assert.Equal(1, f.Model.Calls);
        Assert.StartsWith("pcenc1.", (await db.AgentRuns.SingleAsync()).Output);
        Assert.Empty(await db.Jobs.ToListAsync());
        Assert.Empty(await db.JobChains.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_uncertain_jev_review_does_not_approve_or_discard_usage(bool fail)
    {
        await using var f = new AgentWorkflowFixture();
        await f.SeedAsync();
        var service = f.Service();
        f.Reviewer.Fail = fail;
        f.Reviewer.Score = 0.4;
        await service.SetSubscriptionAsync(f.ProjectId, "services", true);
        var result = await service.RunAsync(f.ProjectId, "services", Guid.NewGuid(), Input);
        Assert.Equal(AgentRunStatus.NeedsReview, result.Status);
        Assert.Equal(1.25m, result.DemoCredits);
        Assert.DoesNotContain("Private upstream", result.Notice);
    }

    [Fact]
    public async Task Missing_usage_is_unknown_not_a_zero_charge_and_tenants_cannot_read_each_others_work()
    {
        await using var f = new AgentWorkflowFixture();
        await f.SeedAsync();
        var service = f.Service();
        f.Model.Completion = f.Model.Completion with
        {
            InputTokens = null,
            OutputTokens = null
        };
        await service.SetSubscriptionAsync(f.ProjectId, "services", true);
        var result = await service.RunAsync(f.ProjectId, "services", Guid.NewGuid(), Input);
        Assert.Null(result.DemoCredits);
        Assert.Null(result.InputTokens);
        var otherId = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        await using (var db = f.Db(otherId))
        {
            db.Projects.Add(new()
            {
                Id = otherProject
            });
            await db.SaveChangesAsync();
        }
        var other = f.Service(otherId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.LoadAsync(f.ProjectId));
        var workspace = await other.LoadAsync(otherProject);
        Assert.Empty(workspace.Runs);
        Assert.Empty(workspace.Subscriptions);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.RunAsync(otherProject, "services", result.Id, Input));
    }

    [Fact]
    public async Task Retrying_an_inflight_request_does_not_execute_or_charge_twice()
    {
        await using var f = new AgentWorkflowFixture();
        await f.SeedAsync();
        var service = f.Service();
        f.Model.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await service.SetSubscriptionAsync(f.ProjectId, "services", true);
        var id = Guid.NewGuid();
        var first = service.RunAsync(f.ProjectId, "services", id, Input);
        await f.Model.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(AgentRunStatus.Running, (await f.Service().RunAsync(f.ProjectId, "services", id, Input)).Status);
            await service.SetSubscriptionAsync(f.ProjectId, "services", false);
        }
        finally
        {
            f.Model.Pause.TrySetResult();
        }
        Assert.Equal(AgentRunStatus.Completed, (await first).Status);
        Assert.Equal(1, f.Model.Calls);
        await using var db = f.Db();
        Assert.Single(await db.UsageRecords.ToListAsync());
    }
}
