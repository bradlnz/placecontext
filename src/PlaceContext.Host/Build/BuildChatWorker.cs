using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Build;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Build;

public sealed class BuildChatWorker(IServiceScopeFactory scopes, ILogger<BuildChatWorker> log, OperationCenter? operations = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = new Dictionary<Guid, Task>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                foreach (var id in active.Where(x => x.Value.IsCompleted).Select(x => x.Key).ToArray())
                { await active[id]; active.Remove(id); }
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTimeOffset.UtcNow;
                    var pending = await db.BuildConversations.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.Status == BuildRunStatus.Queued || (x.Status == BuildRunStatus.Running && x.LeaseUntil <= now))
                        .OrderBy(x => x.UpdatedAt).Take(20).ToListAsync(stoppingToken);
                    foreach (var row in pending)
                    {
                        if (active.Count >= 3) break;
                        if (!active.ContainsKey(row.Id)) active.Add(row.Id, ProcessAsync(row, stoppingToken));
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { log.LogError(ex, "Could not poll background Build runs."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await Task.WhenAll(active.Values); }
    }

    // Runs in its own scope and identity, independent of any Blazor circuit.
    public async Task ProcessAsync(BuildConversationRow candidate, CancellationToken stoppingToken)
    {
        try
        {
            if (candidate.Status == BuildRunStatus.Running)
            {
                await FailUnclaimedAsync(candidate, BuildRunStatus.Interrupted,
                    "The server lost this run. Completed actions are preserved; inspect current resource state before continuing.");
                return;
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == candidate.TenantId, stoppingToken)
                ?? throw new InvalidOperationException("The workspace no longer exists.");
            CurrentTenant.Set(new(tenant.Id, tenant.Slug, tenant.Name, tenant.TimeZoneId));
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == candidate.UserId, stoppingToken)
                ?? throw new InvalidOperationException("The submitting user no longer has workspace access.");
            CurrentUser.Set(new(user.Id, user.Role));
            var store = scope.ServiceProvider.GetRequiredService<IBuildChatStore>();
            var conversation = await store.LoadAsync(candidate.ProjectId, candidate.Id, stoppingToken);
            if (conversation.Status != BuildRunStatus.Queued) return;
            using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var monitor = WatchStopAsync(candidate.Id, run);
            try { await scope.ServiceProvider.GetRequiredService<BuildChatAgent>().RunAsync(conversation, run.Token); }
            finally { await run.CancelAsync(); await monitor; }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Background Build run {ConversationId} failed.", candidate.Id);
            await FailUnclaimedAsync(candidate, BuildRunStatus.Failed, ex.Message);
        }
        finally { CurrentUser.Clear(); CurrentTenant.Clear(); }
    }

    private async Task WatchStopAsync(Guid id, CancellationTokenSource run)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(run.Token))
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await db.BuildConversations.AnyAsync(x => x.Id == id && x.StopRequested, run.Token))
                { await run.CancelAsync(); return; }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not check cancellation for Build run {ConversationId}.", id);
            await run.CancelAsync();
        }
    }

    private async Task FailUnclaimedAsync(BuildConversationRow candidate, BuildRunStatus status, string error)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.BuildConversations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == candidate.Id);
            // A changed Version belongs to a different claim or a later user turn.
            if (row is null || row.Version != candidate.Version) return;
            var encryption = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
            var conversation = JsonSerializer.Deserialize<BuildConversation>(encryption.Unprotect(row.Content, DbBuildChatStore.Purpose))!;
            BuildChatAgent.RepairInterruptedCalls(conversation.Messages);
            conversation.Status = row.Status = status;
            conversation.Error = error;
            conversation.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = error });
            row.Content = encryption.Protect(JsonSerializer.Serialize(conversation), DbBuildChatStore.Purpose);
            row.Lease = null; row.LeaseUntil = null;
            row.Version = Guid.NewGuid(); row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            var operation = operations?.ListForTenant(candidate.TenantId, candidate.UserId)
                .FirstOrDefault(op => op.CorrelationKey == $"build:{candidate.Id}" && op.Status is PortalOperationStatus.Queued or PortalOperationStatus.Running);
            if (operation is not null) operations!.MarkFailed(operation.Id, error);
        }
        catch (DbUpdateConcurrencyException) { }
        catch (Exception ex) { log.LogError(ex, "Could not persist failed Build run {ConversationId}.", candidate.Id); }
    }
}
