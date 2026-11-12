using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Build;

public sealed class ConversationShares(IServiceScopeFactory scopes, ICurrentTenant tenant, ICurrentUser user,
    IPermissionService permissions, IDataEncryptor encryption, PlaceContextService service, ArtifactReader artifacts, IClock clock) : IConversationShares
{
    private const string Purpose = "build.chat.share.v1";
    private const int MaxArtifactBytes = 20 * 1024 * 1024;

    private async Task<BuildConversationRow> OwnedAsync(AppDbContext db, Guid projectId, Guid id, CancellationToken ct)
    {
        if (!tenant.IsResolved || !user.IsAuthenticated || !await permissions.HasAsync(Permission.ProjectsView, ct)
            || !await permissions.HasAsync(Permission.ArtifactsShare, ct))
            throw new UnauthorizedAccessException("Project viewing and public sharing permissions are required.");
        return await db.BuildConversations.SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId && x.UserId == user.UserId, ct)
            ?? throw new InvalidOperationException("Conversation not found.");
    }

    public async Task<WorkflowDemoLink> CreateAsync(Guid projectId, Guid conversationId, Guid? runId, IReadOnlyList<Guid> artifactIds, int lifetimeDays, CancellationToken ct = default)
    {
        if (lifetimeDays is < 1 or > 30 || artifactIds.Count > 20) throw new ArgumentException("Choose 1–30 days and at most 20 artifacts.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await OwnedAsync(db, projectId, conversationId, ct);
        var conversation = JsonSerializer.Deserialize<BuildConversation>(encryption.Unprotect(row.Content, DbBuildChatStore.Purpose))!;
        // Share visible messages only; model instructions, memory and connection settings stay private.
        var messages = new JsonArray();
        foreach (var message in conversation.Messages.OfType<JsonObject>())
        {
            var role = message["role"]?.ToString();
            if (role is not ("user" or "assistant" or "tool")) continue;
            messages.Add(new JsonObject { ["role"] = role, ["content"] = message["content"]?.DeepClone() });
        }
        if (messages.Count == 0) throw new InvalidOperationException("Start a conversation before sharing it.");
        PlaceContext.Application.Dtos.ChainRunView? run = null;
        if (runId is { } selectedRun)
        {
            if (!await permissions.HasAsync(Permission.ChainsManage, ct) || !await permissions.HasAsync(Permission.JobsView, ct))
                throw new UnauthorizedAccessException("Workflow viewing permission is required.");
            run = await service.GetChainRunAsync(selectedRun, ct);
            if (run is null || run.ProjectId != projectId) throw new InvalidOperationException("Workflow run not found in this project.");
        }
        var files = new List<SharedConversationArtifact>();
        var totalBytes = 0;
        foreach (var id in artifactIds.Distinct())
        {
            if (!await permissions.HasAsync(Permission.ArtifactsView, ct)) throw new UnauthorizedAccessException("Artifact viewing permission is required.");
            var file = await artifacts.GetAsync(id, ct);
            if (file is null || file.ProjectId != projectId) throw new InvalidOperationException("Artifact not found in this project.");
            var content = await artifacts.OpenAsync(id, file.RunId, ct) ?? throw new InvalidOperationException("Artifact content is unavailable.");
            await using var source = content.Content;
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(chunk, ct)) > 0)
            {
                totalBytes += count;
                if (totalBytes > MaxArtifactBytes) throw new InvalidOperationException("Shared artifacts must total at most 20 MB.");
                buffer.Write(chunk, 0, count);
            }
            files.Add(new(file.Title, content.ContentType, buffer.ToArray()));
        }
        await OwnedAsync(db, projectId, conversationId, ct);
        var token = "pc_chat_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiry = clock.UtcNow.AddDays(lifetimeDays);
        row.ShareTokenHash = Hash(token);
        row.ShareExpiresAt = expiry;
        row.SharedContent = encryption.Protect(JsonSerializer.Serialize(new SharedConversation(conversation.Title, messages, run, files, clock.UtcNow, expiry)), Purpose);
        await db.SaveChangesAsync(ct);
        return new(token, expiry);
    }

    public async Task<DateTimeOffset?> GetExpiryAsync(Guid projectId, Guid conversationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var row = await OwnedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), projectId, conversationId, ct);
        return row.ShareExpiresAt > clock.UtcNow ? row.ShareExpiresAt : null;
    }

    public async Task RevokeAsync(Guid projectId, Guid conversationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await OwnedAsync(db, projectId, conversationId, ct);
        row.ShareTokenHash = row.SharedContent = null;
        row.ShareExpiresAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<SharedConversation?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (token.Length != 72 || !token.StartsWith("pc_chat_", StringComparison.Ordinal)) return null;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = Hash(token);
        var row = await db.BuildConversations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ShareTokenHash == hash && x.ShareExpiresAt > clock.UtcNow && x.SharedContent != null)
            .Select(x => new { x.ProjectId, x.TenantId, x.SharedContent }).SingleOrDefaultAsync(ct);
        if (row is null || !await db.Projects.IgnoreQueryFilters().AnyAsync(x => x.Id == row.ProjectId && x.TenantId == row.TenantId, ct)) return null;
        return JsonSerializer.Deserialize<SharedConversation>(encryption.Unprotect(row.SharedContent, Purpose));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
