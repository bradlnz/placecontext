namespace PlaceContext.Application.Features;

/// <summary>
/// Shared helpers for turning a local-LLM reply into a portal-ready HTML document: pulling the
/// document out of fences/prose, sanity-checking it, and injecting the portal chart theme so the
/// result reads correctly embedded in a dark portal card (and standalone). Used by the post-job
/// actions (report/chart artifacts) and the Analytics tab.
/// </summary>
public static class LlmHtml
{
    // Portal chart theme, injected into every generated chart so it reads correctly embedded in the
    // dark run-history card (and standalone). !important on the page surface/ink/font defeats the
    // LLM's own <body> background and serif defaults; SVG bar/area fills are left alone, so charts
    // keep their colours while labels and titles become legible. color-scheme + a light media query
    // keep it sensible when the artifact is opened on its own.
    private const string ChartThemeCss =
        "<style id=\"pc-chart-theme\">" +
        ":root{color-scheme:dark light;--pc-bg:#111418;--pc-surface:#161a1f;--pc-ink:#e8ebef;--pc-ink2:#98a2ad;--pc-border:#242b33;--pc-brand:#8b7cff}" +
        "@media (prefers-color-scheme:light){:root{--pc-bg:#fff;--pc-surface:#f4f6f8;--pc-ink:#101620;--pc-ink2:#5a6571;--pc-border:#e5e9ed}}" +
        "html,body{background:var(--pc-bg)!important;color:var(--pc-ink)!important;margin:0!important;" +
        "font-family:'Geist',ui-sans-serif,system-ui,-apple-system,sans-serif!important}" +
        "body{padding:16px 18px!important;box-sizing:border-box}" +
        "h1,h2,h3,h4{color:var(--pc-ink)!important;font-weight:600;line-height:1.25}" +
        "h1{font-size:18px!important;margin:0 0 12px!important}h2{font-size:15px!important}" +
        "h3{font-size:12px!important;color:var(--pc-ink2)!important}" +
        "p,li,figcaption,.meta{color:var(--pc-ink2)!important}" +
        "svg{max-width:100%;height:auto}svg text{fill:var(--pc-ink)!important;" +
        "font-family:'Geist',ui-sans-serif,system-ui,sans-serif!important}" +
        "table{border-collapse:collapse;width:100%;font-size:12.5px}" +
        "th,td{border:1px solid var(--pc-border)!important;padding:6px 9px;text-align:left;color:var(--pc-ink2)!important}" +
        "th{background:var(--pc-surface)!important;color:var(--pc-ink)!important;font-weight:600}" +
        "a{color:var(--pc-brand)!important}code,pre{font-family:'Geist Mono',ui-monospace,monospace!important}" +
        "pre{background:var(--pc-surface)!important;border:1px solid var(--pc-border)!important;border-radius:8px;" +
        "padding:10px;overflow:auto;color:var(--pc-ink2)!important}" +
        "</style>";

    /// <summary>
    /// Inject the portal chart theme after &lt;head&gt; (or synthesise one), so our rules load after
    /// the document's own and win. Falls back to prepending when there's no &lt;html&gt; at all.
    /// </summary>
    public static string StyleChart(string html)
    {
        var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (head >= 0)
        {
            var gt = html.IndexOf('>', head);
            if (gt >= 0) return html[..(gt + 1)] + ChartThemeCss + html[(gt + 1)..];
        }
        var htmlTag = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlTag >= 0)
        {
            var gt = html.IndexOf('>', htmlTag);
            if (gt >= 0) return html[..(gt + 1)] + "<head>" + ChartThemeCss + "</head>" + html[(gt + 1)..];
        }
        return ChartThemeCss + html;
    }

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// Pull the HTML out of an LLM response: take the content of a ``` fenced block if one appears
    /// anywhere, then narrow to the document root (dropping any prose before &lt;!doctype/&lt;html and
    /// after &lt;/html&gt;). Robust to the model wrapping the doc in fences and/or adding commentary.
    /// </summary>
    public static string ExtractHtml(string raw)
    {
        var s = raw.Trim();

        var fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var afterTicks = s.IndexOf('\n', fence);     // skip the ```/```html line
            if (afterTicks >= 0)
            {
                var rest = s[(afterTicks + 1)..];
                var close = rest.IndexOf("```", StringComparison.Ordinal);
                s = (close >= 0 ? rest[..close] : rest).Trim();
            }
        }

        var lower = s.ToLowerInvariant();
        var start = lower.IndexOf("<!doctype", StringComparison.Ordinal);
        if (start < 0) start = lower.IndexOf("<html", StringComparison.Ordinal);
        if (start > 0) s = s[start..];

        var end = s.ToLowerInvariant().LastIndexOf("</html>", StringComparison.Ordinal);
        if (end >= 0) s = s[..(end + "</html>".Length)];

        return s.Trim();
    }

    public static bool LooksLikeHtml(string s) =>
        !string.IsNullOrWhiteSpace(s) && s.Contains('<') &&
        (s.Contains("<html", StringComparison.OrdinalIgnoreCase) || s.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
         || s.Contains("<body", StringComparison.OrdinalIgnoreCase) || s.Contains("<table", StringComparison.OrdinalIgnoreCase)
         || s.Contains("<div", StringComparison.OrdinalIgnoreCase) || s.Contains("<h1", StringComparison.OrdinalIgnoreCase)
         || s.Contains("<ul", StringComparison.OrdinalIgnoreCase) || s.Contains("<svg", StringComparison.OrdinalIgnoreCase));
}
