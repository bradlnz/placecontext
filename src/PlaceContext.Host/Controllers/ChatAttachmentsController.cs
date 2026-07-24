using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Streams a chat-uploaded attachment from the object store. The chat page uploads bytes to the
/// <c>chat-attachments</c> bucket under keys shaped
/// <c>chat/{tenantId}/{projectId}/{sessionId}/{guid}-{safeFileName}</c>; the {tenantId} segment must
/// match the caller's tenant (<see cref="ICurrentTenant"/>) so one tenant cannot read another's
/// attachments. Previewable types render inline under the same CSP sandbox as post-job artifacts
/// (<see cref="InlinePreview"/>); everything else downloads as an attachment.
/// </summary>
[Authorize]
public sealed class ChatAttachmentsController : ControllerBase
{
    /// <summary>The bucket the chat uploader writes attachment bytes to.</summary>
    public const string Bucket = "chat-attachments";

    private readonly ICurrentTenant _tenant;
    private readonly IObjectStore _store;

    public ChatAttachmentsController(ICurrentTenant tenant, IObjectStore store)
    {
        _tenant = tenant;
        _store = store;
    }

    [HttpGet("/chat/attachments/{**key}")]
    public async Task<IActionResult> Get(string key)
    {
        // Key shape written by the chat uploader:
        //   chat/{tenantId}/{projectId}/{sessionId}/{guid}-{safeFileName}
        if (string.IsNullOrWhiteSpace(key)) return NotFound();
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 5) return NotFound();
        if (!string.Equals(segments[0], "chat", StringComparison.Ordinal)) return NotFound();

        // Cross-tenant guard: the tenant id embedded in the key must be the caller's tenant.
        // 404 (not 403), matching ArtifactsController — an unauthorized key looks like a missing one.
        if (!Guid.TryParse(segments[1], out var keyTenantId)) return NotFound();
        if (!_tenant.IsResolved || keyTenantId != _tenant.TenantId) return NotFound();

        if (!_store.IsEnabled) return NotFound();
        var obj = await _store.OpenReadAsync(Bucket, key, HttpContext.RequestAborted);
        if (obj is null) return NotFound();

        var contentType = string.IsNullOrWhiteSpace(obj.ContentType)
            ? "application/octet-stream"
            : obj.ContentType;

        // The last segment embeds the original file name after the uploader's "{guid}-" prefix
        // (36-char dashed or 32-char "N" form). Anything else falls back to a generic name.
        var last = segments[^1];
        var fileName = "attachment";
        foreach (var guidLen in new[] { 36, 32 })
        {
            if (last.Length > guidLen + 1 && last[guidLen] == '-' && Guid.TryParse(last[..guidLen], out _))
            {
                fileName = last[(guidLen + 1)..];
                break;
            }
        }

        return InlinePreview.StreamResult(Response, obj.Content, contentType, fileName);
    }
}
