using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Shared;
using PlaceContext.Host.Controllers;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Fact]
    public async Task Conversation_shares_are_owned_encrypted_fixed_expiring_and_revocable()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var clock = new ShareClock();
        var shares = new ConversationShares(f.Provider.GetRequiredService<IServiceScopeFactory>(), f.Tenant, f.User, f.Permissions,
            f.Provider.GetRequiredService<IDataEncryptor>(), f.Service, null!, clock);
        var chat = new BuildConversation { ProjectId = f.ProjectId, Title = "Report conversation", SystemPrompt = "Private model instructions" };
        var lease = await f.Store.AcquireAsync(chat);
        chat.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Create a report." });
        chat.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = "Here is your report." });
        await f.Store.SaveAsync(chat, lease, true);
        var link = await shares.CreateAsync(f.ProjectId, chat.Id, null, [], 7);
        var row = await CandidateAsync(f, chat.Id);
        Assert.DoesNotContain("Create a report", row.SharedContent);
        Assert.NotEqual(link.Token, row.ShareTokenHash);
        var snapshot = Assert.IsType<SharedConversation>(await shares.ResolveAsync(link.Token));
        Assert.Equal(2, snapshot.Messages.Count);
        Assert.DoesNotContain("Private model instructions", System.Text.Json.JsonSerializer.Serialize(snapshot));
        lease = await f.Store.AcquireAsync(chat);
        chat.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Later private message" });
        await f.Store.SaveAsync(chat, lease, true);
        Assert.Equal(2, (await shares.ResolveAsync(link.Token))!.Messages.Count);
        var originalUser = f.User.UserId;
        f.User.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shares.CreateAsync(f.ProjectId, chat.Id, null, [], 7));
        Assert.NotNull(await shares.ResolveAsync(link.Token));
        f.User.UserId = originalUser;
        f.Permissions.Denied = Permission.ArtifactsShare;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shares.CreateAsync(f.ProjectId, chat.Id, null, [], 7));
        f.Permissions.Denied = null;
        var next = await shares.CreateAsync(f.ProjectId, chat.Id, null, [], 1);
        Assert.Null(await shares.ResolveAsync(link.Token));
        clock.UtcNow = clock.UtcNow.AddDays(2);
        Assert.Null(await shares.ResolveAsync(next.Token));
        next = await shares.CreateAsync(f.ProjectId, chat.Id, null, [], 1);
        await shares.RevokeAsync(f.ProjectId, chat.Id);
        Assert.Null(await shares.ResolveAsync(next.Token));
        next = await shares.CreateAsync(f.ProjectId, chat.Id, null, [], 1);
        await f.Store.DeleteAsync(f.ProjectId, chat.Id);
        Assert.Null(await shares.ResolveAsync(next.Token));
    }

    private sealed class ShareClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }

    [Fact]
    public async Task Shared_conversations_reuse_chat_styles_and_render_untrusted_content_without_scripts_or_live_actions()
    {
        var shares = new ConversationSharesStub { Snapshot = new("<script>title</script>",
            [new JsonObject { ["role"] = "user", ["content"] = "</div><script>alert(1)</script>" },
             new JsonObject { ["role"] = "assistant", ["content"] = "**Report** [bad](javascript:alert(1))" }], null,
            [new("report.pdf", "application/pdf", "%PDF-test"u8.ToArray()), new("bad.html", "text/html", "<script>bad</script>"u8.ToArray())], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)) };
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var controller = new ConversationShareController(shares, NullLoggerFactory.Instance) { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = provider } } };
        var result = Assert.IsType<ContentResult>(await controller.Get("token"));
        Assert.Contains("/conversation.css", result.Content);
        Assert.Contains("chat-message user", result.Content);
        Assert.Contains("message-markdown", result.Content);
        Assert.Contains("<strong>Report</strong>", result.Content);
        Assert.DoesNotContain("<script>", result.Content);
        Assert.DoesNotContain("javascript:", result.Content);
        Assert.DoesNotContain("chat-composer", result.Content);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Contains("sandbox", controller.Response.Headers.ContentSecurityPolicy.ToString());
        var pdf = Assert.IsType<FileContentResult>(await controller.Artifact("token", 0));
        Assert.Equal("application/pdf", pdf.ContentType);
        var html = Assert.IsType<FileContentResult>(await controller.Artifact("token", 1));
        Assert.Equal("application/octet-stream", html.ContentType);
        Assert.NotEmpty(html.FileDownloadName);
        shares.Snapshot = null;
        Assert.IsType<NotFoundResult>(await controller.Get("token"));
        Assert.IsType<NotFoundResult>(await controller.Artifact("token", 0));
    }

    private sealed class ConversationSharesStub : IConversationShares
    {
        public SharedConversation? Snapshot;
        public Task<SharedConversation?> ResolveAsync(string token, CancellationToken ct = default) => Task.FromResult(Snapshot);
        public Task<WorkflowDemoLink> CreateAsync(Guid projectId, Guid conversationId, Guid? runId, IReadOnlyList<Guid> artifactIds, int lifetimeDays, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DateTimeOffset?> GetExpiryAsync(Guid projectId, Guid conversationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(Guid projectId, Guid conversationId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
