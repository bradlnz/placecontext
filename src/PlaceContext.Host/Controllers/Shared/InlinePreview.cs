using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Shared inline-preview policy for streaming stored bytes back to the browser (post-job artifacts,
/// chat attachments). Previewable types render inline — HTML/SVG additionally under a CSP sandbox —
/// and everything else downloads as an attachment. The response headers set here are a security
/// contract; the portal's iframe previews depend on them exactly as written.
/// </summary>
internal static class InlinePreview
{
    /// <summary>
    /// Sets the shared security/preview headers on <paramref name="response"/> and returns the
    /// streaming result for <paramref name="content"/>.
    /// </summary>
    public static FileStreamResult StreamResult(
        HttpResponse response, Stream content, string contentType, string fileName)
    {
        var isHtml = IsHtmlOrSvg(contentType);
        var inline = CanPreviewInline(contentType);

        response.Headers["X-Content-Type-Options"] = "nosniff";
        // Portal embeds previews in an iframe on the same host (including public DNS names).
        // Override the global baseline if anything else set DENY, and allow same-origin framing.
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        // Explicit CSP frame-ancestors for modern browsers (pairs with XFO above).
        // The sandbox MUST be this response header, not a sandbox attribute on the portal's iframe:
        // the attribute gives the frame an opaque origin before the fetch, so the browser withholds
        // the SameSite=Lax auth cookie and this [Authorize] endpoint bounces to /locked (blank
        // preview). The header applies after the authenticated fetch — scripts are still stripped
        // and the document still gets an opaque origin. allow-popups lets links in HTML reports
        // open in a new tab (the opened page stays sandboxed).
        response.Headers["Content-Security-Policy"] = isHtml
            ? "sandbox allow-popups; frame-ancestors 'self'"
            : "frame-ancestors 'self'";

        if (inline)
        {
            // Content-Disposition: inline so the iframe renders; filename still available for "Save as".
            response.Headers["Content-Disposition"] =
                $"inline; filename=\"{fileName.Replace("\"", "")}\"";
            // Keep real content type for images/PDF/text so the browser can render.
            // HTML is served as text/html under CSP sandbox (not as the portal document).
            return new FileStreamResult(content, isHtml ? "text/html; charset=utf-8" : contentType);
        }

        return new FileStreamResult(content, contentType) { FileDownloadName = fileName };
    }

    public static bool IsHtmlOrSvg(string contentType) =>
        contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Types the portal may show in an iframe (must stay in sync with
    /// <c>Artifacts.razor</c> <c>Previewable</c>).
    /// </summary>
    public static bool CanPreviewInline(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
