using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Build;

public sealed class DbBuildChatStore(IServiceScopeFactory scopes, ICurrentUser user, ICurrentTenant tenant,
    IPermissionService permissions, IDataEncryptor encryption) : IBuildChatStore
{
    public const string Purpose = "build.chat.v1";
    private async Task CheckAsync(AppDbContext db, Guid projectId, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !tenant.IsResolved || !await permissions.HasAsync(Permission.ProjectsView, ct))
        {
            throw new UnauthorizedAccessException("Project access is required.");
        }
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new InvalidOperationException("Project not found.");
        }
    }
    private IQueryable<BuildConversationRow> Owned(AppDbContext db, Guid projectId) =>
        db.BuildConversations.Where(x => x.UserId == user.UserId && x.ProjectId == projectId);
    private BuildConversation Read(BuildConversationRow row)
    {
        var conversation = JsonSerializer.Deserialize<BuildConversation>(encryption.Unprotect(row.Content, Purpose))!;
        conversation.Status = row.Status;
        conversation.StopRequested = row.StopRequested;
        return conversation;
    }

    public async Task<IReadOnlyList<BuildConversationSummary>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var rows = await Owned(db, projectId).AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(100).Select(x => new { x.Id, x.Title, x.UpdatedAt, x.Status }).ToListAsync(ct);
        return rows.Select(row => new BuildConversationSummary(row.Id, encryption.Unprotect(row.Title, Purpose), row.UpdatedAt, row.Status)).ToList();
    }
    public async Task<IReadOnlyList<BuildConversationSummary>> ListAgentSessionsAsync(Guid projectId, Guid agentId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        // Agent identity is encrypted in the session; inspect the same recent window as session history.
        var rows = await Owned(db, projectId).AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(100).ToListAsync(ct);
        return rows.Where(row =>
            {
                using var content = JsonDocument.Parse(encryption.Unprotect(row.Content, Purpose));
                if (!content.RootElement.TryGetProperty("AgentId", out var value) || value.ValueKind == JsonValueKind.Null)
                    return agentId == DashboardSettings.DefaultAgentId;
                return value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)
                    && (id == Guid.Empty ? DashboardSettings.DefaultAgentId : id) == agentId;
            })
            .Select(row => new BuildConversationSummary(row.Id, encryption.Unprotect(row.Title, Purpose), row.UpdatedAt, row.Status)).ToArray();
    }

    public async Task<BuildConversation> LoadAsync(Guid projectId, Guid id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await Owned(db, projectId).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Session not found.");
        return Read(row);
    }
    public async Task QueueAsync(BuildConversation conversation, JsonObject message, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, conversation.ProjectId, ct);
        var connection = await db.ModelConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversation.ConnectionId && x.Enabled, ct);
        if (connection is null || !JsonSerializer.Deserialize<string[]>(connection.ModelsJson)!.Contains(conversation.Model))
        {
            throw new InvalidOperationException("Choose an available model before sending.");
        }
        var row = await Owned(db, conversation.ProjectId).SingleOrDefaultAsync(x => x.Id == conversation.Id, ct);
        BuildConversationRow ApplyConversation()
        {
            if (row is null)
            {
                row = new()
                {
                    Id = conversation.Id,
                    ProjectId = conversation.ProjectId,
                    UserId = user.UserId
                };
                db.BuildConversations.Add(row);
                return row;
            }
            {
                if (row.Status is BuildRunStatus.Queued or BuildRunStatus.Running || row.LeaseUntil > DateTimeOffset.UtcNow)
                {
                    throw new InvalidOperationException("This session already has a background run.");
                }
                var saved = Read(row);
                conversation.Messages = saved.Messages;
                conversation.Title = saved.Title;
            }

            return row;
        }
        row = ApplyConversation();
        if (message["role"]?.GetValue<string>() != "user")
        {
            throw new ArgumentException("A user message is required.");
        }
        if (conversation.Messages.Count > 180)
        {
            throw new InvalidOperationException("This session is full. Start a new chat.");
        }
        var messages = conversation.Messages.DeepClone().AsArray();
        messages.Add(message.DeepClone());
        if (messages.ToJsonString().Length > 14_000_000)
        {
            throw new InvalidOperationException("This session is full. Start a new chat or use smaller images.");
        }
        conversation.Messages = messages;
        if (messages.Count == 1)
        {
            var text = (message["content"] as JsonArray)?.FirstOrDefault(x => x?["type"]?.GetValue<string>() == "text")?["text"]?.GetValue<string>() ?? "Image session";
            conversation.Title = text[..Math.Min(80, text.Length)];
        }
        conversation.Status = row.Status = BuildRunStatus.Queued;
        conversation.Error = null;
        conversation.StopRequested = row.StopRequested = false;
        row.Lease = null;
        row.LeaseUntil = null;
        row.Content = encryption.Protect(JsonSerializer.Serialize(conversation), Purpose);
        row.Title = encryption.Protect(conversation.Title, Purpose);
        row.Version = Guid.NewGuid();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("This session changed. Open it again before sending.");
        }
    }

    public async Task RequestStopAsync(Guid projectId, Guid id, CancellationToken ct = default)
    {
        // Do not alter Version: cancellation must not invalidate a worker's progress save.
        for (var attempt = 0; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CheckAsync(db, projectId, ct);
            var row = await Owned(db, projectId).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Session not found.");
            if (row.Status is not (BuildRunStatus.Queued or BuildRunStatus.Running))
            {
                return;
            }
            row.StopRequested = true;
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
        }
    }

    public async Task<Guid> AcquireAsync(BuildConversation conversation, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, conversation.ProjectId, ct);
        var row = await Owned(db, conversation.ProjectId).SingleOrDefaultAsync(x => x.Id == conversation.Id, ct);
        BuildConversationRow ApplyConversation()
        {
            if (row is null)
            {
                row = new()
                {
                    Id = conversation.Id,
                    ProjectId = conversation.ProjectId,
                    UserId = user.UserId,
                    Content = encryption.Protect(JsonSerializer.Serialize(conversation), Purpose)
                };
                db.BuildConversations.Add(row);
                return row;
            }
            {
                if (row.LeaseUntil > DateTimeOffset.UtcNow)
                {
                    throw new InvalidOperationException("This chat is already responding in the background.");
                }
                if (conversation.Status == BuildRunStatus.Queued && row.Status != BuildRunStatus.Queued)
                {
                    throw new InvalidOperationException("This background run was already claimed.");
                }
                var saved = Read(row);
                // Reload under the lease so stale browser tabs cannot overwrite a completed turn.
                conversation.Messages = saved.Messages;
                conversation.Title = saved.Title;
                conversation.StopRequested = row.StopRequested;
            }

            return row;
        }
        row = ApplyConversation();
        conversation.Status = row.Status = BuildRunStatus.Running;
        row.Lease = Guid.NewGuid();
        row.Version = Guid.NewGuid();
        row.LeaseUntil = DateTimeOffset.UtcNow.Add(BuildExecutionLimits.Lease);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This chat changed in another tab. Open it again.");
        }
        return row.Lease.Value;
    }

    public async Task<bool> RenewAsync(Guid projectId, Guid id, Guid lease, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !tenant.IsResolved)
        {
            return false;
        }
        for (var attempt = 0; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await Owned(db, projectId).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null || row.Lease != lease || row.Status != BuildRunStatus.Running
                || row.StopRequested || row.LeaseUntil <= DateTimeOffset.UtcNow)
            {
                return false;
            }
            row.LeaseUntil = DateTimeOffset.UtcNow.Add(BuildExecutionLimits.Lease);
            // Do not change Version: a pulse must not conflict with the agent saving its progress.
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
        }
    }
    public async Task SaveAsync(BuildConversation conversation, Guid lease, bool release, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The existing owned lease permits recording a terminal outcome even after permissions change.
        if (!user.IsAuthenticated || !tenant.IsResolved)
        {
            throw new UnauthorizedAccessException("Sign in to save a session.");
        }
        var row = await Owned(db, conversation.ProjectId).SingleAsync(x => x.Id == conversation.Id, ct);
        if (row.Lease != lease || row.LeaseUntil <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The chat session expired. Open the session again before continuing.");
        }
        if (release && conversation.Status == BuildRunStatus.Running)
        {
            conversation.Status = BuildRunStatus.Completed;
        }
        var json = JsonSerializer.Serialize(conversation);
        if (json.Length > 16_000_000)
        {
            throw new InvalidOperationException("This session is full. Start a new chat.");
        }
        row.Content = encryption.Protect(json, Purpose);
        row.Title = encryption.Protect(conversation.Title, Purpose);
        row.Version = Guid.NewGuid();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (release)
        {
            row.Status = conversation.Status;
            row.Lease = null;
            row.LeaseUntil = null;
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteAsync(Guid projectId, Guid id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await Owned(db, projectId).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return;
        }
        if (row.Status is BuildRunStatus.Queued or BuildRunStatus.Running || row.LeaseUntil > DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Stop the background run before deleting this chat.");
        }
        db.BuildConversations.Remove(row);
        await db.SaveChangesAsync(ct);
    }
    public async Task<string> GetMemoryAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await db.BuildMemories.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId && x.ProjectId == projectId, ct);
        return encryption.Unprotect(row?.Content, Purpose);
    }
    public async Task SaveMemoryAsync(Guid projectId, string memory, CancellationToken ct = default)
    {
        if (memory.Length > 8000)
        {
            throw new ArgumentException("Memory is limited to 8,000 characters.");
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await db.BuildMemories.SingleOrDefaultAsync(x => x.UserId == user.UserId && x.ProjectId == projectId, ct);
        if (row is null)
        {
            row = new()
            {
                UserId = user.UserId,
                ProjectId = projectId
            };
            db.BuildMemories.Add(row);
        }
        row.Content = encryption.Protect(memory, Purpose);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DashboardSettings> GetSettingsAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await db.BuildMemories.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId && x.ProjectId == projectId, ct);
        return string.IsNullOrEmpty(row?.Settings) ? new() : JsonSerializer.Deserialize<DashboardSettings>(encryption.Unprotect(row.Settings, Purpose))!;
    }

    public async Task SaveSettingsAsync(Guid projectId, DashboardSettings settings, CancellationToken ct = default)
    {
        settings.Validate();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await db.BuildMemories.SingleOrDefaultAsync(x => x.UserId == user.UserId && x.ProjectId == projectId, ct);
        var previous = string.IsNullOrEmpty(row?.Settings) ? new DashboardSettings()
            : JsonSerializer.Deserialize<DashboardSettings>(encryption.Unprotect(row.Settings, Purpose))!;
        // Deleted links must not block dashboard saves; validate only newly linked workflows.
        var workflows = settings.Agents.SelectMany(a => a.WorkflowIds.Except(previous.Agents.FirstOrDefault(p => p.Id == a.Id)?.WorkflowIds ?? [])).Distinct().ToArray();
        if (workflows.Length > 0 && await db.JobChains.CountAsync(w => w.ProjectId == projectId && workflows.Contains(w.Id), ct) != workflows.Length)
            throw new ArgumentException("Link only available workflows from this project.");
        var assignedMcp = settings.Agents.SelectMany(a => a.McpConnectionIds).Distinct().ToArray();
        if (assignedMcp.Length > 0 && await db.McpConnections.CountAsync(c => c.ProjectId == projectId
                && c.Enabled && assignedMcp.Contains(c.Id), ct) != assignedMcp.Length)
        {
            throw new ArgumentException("Assign only enabled MCP connections from this project. Remove unavailable connections before saving.");
        }
        if (row is null)
        {
            row = new() { UserId = user.UserId, ProjectId = projectId };
            db.BuildMemories.Add(row);
        }
        row.Settings = encryption.Protect(JsonSerializer.Serialize(settings), Purpose);
        await db.SaveChangesAsync(ct);
    }
}
