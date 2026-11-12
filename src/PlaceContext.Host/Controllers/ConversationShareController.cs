using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlaceContext.Application.Build;
using PlaceContext.Host.Components.Shared;

namespace PlaceContext.Host.Controllers;

[ApiController, AllowAnonymous, EnableRateLimiting("artifact-share")]
public sealed class ConversationShareController(IConversationShares shares, ILoggerFactory loggerFactory) : ControllerBase
{
    private void Headers()
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox allow-scripts allow-same-origin allow-downloads; default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self'; script-src 'self'; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";
    }

    [HttpGet("/share/conversations/{token}")]
    public async Task<IActionResult> Get(string token)
    {
        Headers();
        var conversation = await shares.ResolveAsync(token, HttpContext.RequestAborted);
        if (conversation is null) return NotFound();
        await using var renderer = new HtmlRenderer(HttpContext.RequestServices, loggerFactory);
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SharedConversationView>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Conversation"] = conversation, ["Token"] = token }))).ToHtmlString());
        return Content($$"""
            <!doctype html><html lang="en" data-pc-theme="light"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{WebUtility.HtmlEncode(conversation.Title)}} — PlaceContext</title>
            <link rel="stylesheet" href="/app.css"><link rel="stylesheet" href="/PlaceContext.Host.styles.css"><link rel="stylesheet" href="/conversation.css"><link rel="stylesheet" href="/shared-conversation.css"></head><body>{{html}}<script defer src="/placecontext.js"></script><script defer src="/shared-conversation.js"></script></body></html>
            """, "text/html; charset=utf-8");
    }

    [HttpGet("/share/conversations/{token}/artifacts/{index:int}")]
    public async Task<IActionResult> Artifact(string token, int index)
    {
        Headers();
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; frame-ancestors 'self'";
        var conversation = await shares.ResolveAsync(token, HttpContext.RequestAborted);
        if (conversation is null || index < 0 || index >= conversation.Artifacts.Count) return NotFound();
        var file = conversation.Artifacts[index];
        var type = file.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        return type is "application/pdf" or "image/png" or "image/jpeg" or "image/webp" or "text/plain"
            ? File(file.Content, type)
            : File(file.Content, "application/octet-stream", "artifact");
    }
}
