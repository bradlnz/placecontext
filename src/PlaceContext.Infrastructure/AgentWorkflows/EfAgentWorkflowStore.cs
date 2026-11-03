using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.AgentWorkflows;
using PlaceContext.Infrastructure.Persistence;
namespace PlaceContext.Infrastructure.AgentWorkflows;

public sealed class EfAgentWorkflowStore(AppDbContext db) : IAgentWorkflowStore
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId, ct);
    public async Task<IReadOnlyList<AgentSubscription>> ListSubscriptionsAsync(CancellationToken ct) =>
        await db.AgentSubscriptions.AsNoTracking().Select(x => new AgentSubscription(x.AgentId, x.Active, x.UpdatedAt)).ToListAsync(ct);
    public async Task<IReadOnlyList<AgentWorkflowRun>> ListRunsAsync(Guid projectId, CancellationToken ct) =>
        (await db.AgentRuns.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.StartedAt).Take(50).ToListAsync(ct)).Select(Map).ToArray();
    public async Task SetSubscriptionAsync(string agentId, bool active, DateTimeOffset updatedAt, CancellationToken ct)
    {
        var row = await db.AgentSubscriptions.SingleOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (row is null)
        {
            row = new()
            {
                AgentId = agentId
            };
            db.AgentSubscriptions.Add(row);
        }
        row.Active = active;
        row.UpdatedAt = updatedAt;
        await db.SaveChangesAsync(ct);
    }
    public Task<bool> IsSubscribedAsync(string agentId, CancellationToken ct) => db.AgentSubscriptions.AnyAsync(x => x.AgentId == agentId && x.Active, ct);
    public async Task<AgentWorkflowRun?> GetRunAsync(Guid id, CancellationToken ct)
    {
        var row = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : Map(row);
    }
    public async Task<bool> TryCreateRunAsync(AgentWorkflowRun run, CancellationToken ct)
    {
        var row = new AgentRunRow();
        db.AgentRuns.Add(row).CurrentValues.SetValues(run);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.AgentRuns.AnyAsync(x => x.Id == run.Id, ct))
            {
                throw;
            }
            return false;
        }
    }
    public async Task SaveRunAsync(AgentWorkflowRun run, AgentWorkflowUsage? usage, CancellationToken ct)
    {
        var row = await db.AgentRuns.SingleAsync(x => x.Id == run.Id, ct);
        db.Entry(row).CurrentValues.SetValues(run);
        if (usage is not null)
        {
            db.UsageRecords.Add(new()
            {
                Id = Guid.NewGuid(),
                ProjectId = usage.ProjectId,
                Model = usage.Model,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                Description = usage.Description,
                RecordedAt = usage.RecordedAt
            });
        }
        await db.SaveChangesAsync(ct);
    }
    private static AgentWorkflowRun Map(AgentRunRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        AgentId = row.AgentId,
        InputHash = row.InputHash,
        Status = row.Status,
        Output = row.Output,
        Notice = row.Notice,
        InputTokens = row.InputTokens,
        OutputTokens = row.OutputTokens,
        DemoCredits = row.DemoCredits,
        StartedAt = row.StartedAt,
        FinishedAt = row.FinishedAt
    };
}
