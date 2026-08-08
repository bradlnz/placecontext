using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Presentation;

namespace PlaceContext.Artifacts.Controllers;

/// <summary>
/// Streams a chat-uploaded attachment after verifying that the tenant embedded in its object key
/// matches the authenticated request tenant.
/// </summary>
[Authorize(Policy = Permission.AgentsChat)]
public sealed class ChatAttachmentsController(
    ICurrentTenant tenant,
    IObjectStore store) : ControllerBase
{
    public const string Bucket = "chat-attachments";

    [HttpGet("/chat/attachments/{**key}")]
    public async Task<IActionResult> Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return NotFound();
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 5) return NotFound();
        if (!string.Equals(segments[0], "chat", StringComparison.Ordinal)) return NotFound();

        if (!Guid.TryParse(segments[1], out var keyTenantId)) return NotFound();
        if (!tenant.IsResolved || keyTenantId != tenant.TenantId) return NotFound();

        if (!store.IsEnabled) return NotFound();
        var stored = await store.OpenReadAsync(Bucket, key, HttpContext.RequestAborted);
        if (stored is null) return NotFound();

        var contentType = string.IsNullOrWhiteSpace(stored.ContentType)
            ? "application/octet-stream"
            : stored.ContentType;

        var last = segments[^1];
        var fileName = "attachment";
        foreach (var guidLength in new[] { 36, 32 })
        {
            if (last.Length > guidLength + 1
                && last[guidLength] == '-'
                && Guid.TryParse(last[..guidLength], out _))
            {
                fileName = last[(guidLength + 1)..];
                break;
            }
        }

        return InlineArtifactPreview.StreamResult(
            Response,
            stored.Content,
            contentType,
            fileName);
    }
}
