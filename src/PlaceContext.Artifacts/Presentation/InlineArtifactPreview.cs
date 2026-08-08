using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Artifacts.Presentation;

/// <summary>
/// Applies the security headers required to safely preview untrusted artifact content inline.
/// </summary>
public static class InlineArtifactPreview
{
    public static FileStreamResult StreamResult(
        HttpResponse response,
        Stream content,
        string contentType,
        string fileName)
    {
        var isHtml = IsHtmlOrSvg(contentType);
        var inline = CanPreviewInline(contentType);

        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        response.Headers["Content-Security-Policy"] = isHtml
            ? "sandbox allow-popups; frame-ancestors 'self'"
            : "frame-ancestors 'self'";

        if (inline)
        {
            response.Headers["Content-Disposition"] =
                $"inline; filename=\"{fileName.Replace("\"", "")}\"";
            return new FileStreamResult(
                content,
                isHtml ? "text/html; charset=utf-8" : contentType);
        }

        return new FileStreamResult(content, contentType) { FileDownloadName = fileName };
    }

    public static bool IsHtmlOrSvg(string contentType)
        => contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    public static bool CanPreviewInline(string contentType)
        => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
}
