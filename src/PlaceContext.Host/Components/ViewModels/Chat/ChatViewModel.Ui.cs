using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    public ElementReference MessagesElement { get; set; }
    public ElementReference ScrollAnchorElement { get; set; }
    public ElementReference DropZoneElement { get; set; }

    public Task SendFromUiAsync() =>
        SendAsync(
            () => ScrollToBottomAsync(MessagesElement),
            () => ScrollAfterRenderAsync(ScrollAnchorElement)
        );

    public Task StopFromUiAsync() => StopAsync();

    public Task OpenFilePicker() => OpenFilePickerAsync();

    public void ToggleSidePanel()
    {
        ShowSidePanel = !ShowSidePanel;
        NotifyStateChanged();
    }

    public Task StarterPromptFromUiAsync(string prompt) =>
        StarterPromptAsync(prompt, () => ScrollToBottomAsync(MessagesElement));

    public Task QuickActionFromUiAsync(string context, string action) =>
        QuickActionAsync(context, action, () => ScrollToBottomAsync(MessagesElement));

    public Task SubmitOptionsFromUiAsync(int messageId) =>
        SubmitOptionsAsync(messageId, MessagesElement, ScrollAnchorElement);

    public void SelectSettingsTab(string tab)
    {
        SettingsTab = tab;
        NotifyStateChanged();
    }

    public void SelectSettingsTab(ChatSettingsTab tab) =>
        SelectSettingsTab(ChatPresentationCatalog.SettingsKey(tab));

    public void ToggleAddCommand()
    {
        ShowAddCommand = !ShowAddCommand;
        NotifyStateChanged();
    }

    public void HideAddCommand()
    {
        ShowAddCommand = false;
        NotifyStateChanged();
    }

    public string FileLabel(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "FILE";

        var extension = Path.GetExtension(fileName).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension)
            ? "FILE"
            : extension[..Math.Min(4, extension.Length)].ToUpperInvariant();
    }

    public string StatusClass(Enum status) => status.ToString().ToLowerInvariant();

    public string SessionTime(DateTimeOffset value) => value.ToString("MMM d HH:mm");

    public string ToolTime(DateTimeOffset value) => value.ToString("HH:mm:ss");

    public string TemperatureLabel => PendingTemperature.ToString("0.0");

    public RenderFragment RenderArtifact(ToolCallInfo tc) =>
        builder =>
        {
            ChatArtifactRenderInfo? info = null;
            try
            {
                info = System.Text.Json.JsonSerializer.Deserialize<ChatArtifactRenderInfo>(
                    tc.Result ?? "{}"
                );
            }
            catch { }

            if (info is null)
            {
                builder.AddMarkupContent(
                    0,
                    "<div class=\"artifact-error\">Couldn't parse artifact result.</div>"
                );
                return;
            }

            var url = $"/runs/{info.RunId}/artifacts/{info.Id}";
            var header = $"""
                <div class="artifact-header">
                    <span class="artifact-title">{System.Net.WebUtility.HtmlEncode(
                    info.Title
                )}</span>
                    <span class="artifact-ct">{System.Net.WebUtility.HtmlEncode(
                    info.ContentType
                )}</span>
                    <span class="artifact-size">{Presentation.Bytes(info.SizeBytes)}</span>
                    <a class="artifact-open" href="{url}" target="_blank" rel="noopener">Open ↗</a>
                </div>
                """;
            if (info.Truncated)
                header +=
                    "<div class='artifact-truncated'>Preview truncated — use Open ↗ for the full file.</div>";
            builder.AddMarkupContent(0, header);

            if (!string.IsNullOrEmpty(info.ExtractedText))
            {
                if (info.ContentType == "application/pdf")
                    builder.AddMarkupContent(1, PdfPreviewMarkup(info, url));
                var details = $"""
                    <details class="artifact-extracted">
                        <summary>Extracted text {(
                        info.ExtractedTruncated ? "(truncated)" : ""
                    )}</summary>
                        <pre class="artifact-pre">{System.Net.WebUtility.HtmlEncode(
                        info.ExtractedText
                    )}</pre>
                    </details>
                    """;
                builder.AddMarkupContent(2, details);
            }
            else if (info.IsText && !string.IsNullOrEmpty(info.Content))
            {
                if (IsJson(info.ContentType))
                {
                    builder.OpenElement(1, "div");
                    builder.AddAttribute(2, "class", "artifact-json");
                    try
                    {
                        var el = System
                            .Text.Json.JsonDocument.Parse(info.Content)
                            .RootElement.Clone();
                        builder.AddMarkupContent(3, RenderJson(el, 0));
                    }
                    catch
                    {
                        builder.AddMarkupContent(
                            3,
                            $"<pre class='artifact-pre'>{System.Net.WebUtility.HtmlEncode(info.Content)}</pre>"
                        );
                    }
                    builder.CloseElement();
                }
                else if (IsCsv(info.ContentType))
                {
                    var parsed = ParseCsv(info.Content);
                    if (parsed.Header.Count == 0)
                    {
                        builder.AddMarkupContent(
                            1,
                            $"<pre class='artifact-pre'>{System.Net.WebUtility.HtmlEncode(info.Content)}</pre>"
                        );
                    }
                    else
                    {
                        var csvSb = new System.Text.StringBuilder();
                        csvSb.Append("<table class='artifact-table'><thead><tr>");
                        foreach (var h in parsed.Header)
                            csvSb.Append($"<th>{System.Net.WebUtility.HtmlEncode(h)}</th>");
                        csvSb.Append("</tr></thead><tbody>");
                        foreach (var row in parsed.Rows)
                        {
                            csvSb.Append("<tr>");
                            foreach (var cell in row)
                                csvSb.Append($"<td>{System.Net.WebUtility.HtmlEncode(cell)}</td>");
                            csvSb.Append("</tr>");
                        }
                        csvSb.Append("</tbody></table>");
                        builder.AddMarkupContent(1, csvSb.ToString());
                    }
                }
                else
                {
                    builder.AddMarkupContent(
                        1,
                        $"<pre class='artifact-pre'>{System.Net.WebUtility.HtmlEncode(info.Content)}</pre>"
                    );
                }
            }
            else if (PreviewableArtifact(info.ContentType))
            {
                if (info.ContentType.StartsWith("image/"))
                    builder.AddMarkupContent(
                        1,
                        $"<img class='artifact-image' src='{url}' alt='{System.Net.WebUtility.HtmlEncode(info.Title)}' loading='lazy' />"
                    );
                else if (info.ContentType.StartsWith("video/"))
                    builder.AddMarkupContent(
                        1,
                        $"<video class='artifact-frame' src='{url}' controls preload='metadata' />"
                    );
                else if (info.ContentType == "application/pdf")
                    builder.AddMarkupContent(1, PdfPreviewMarkup(info, url));
                else
                    builder.AddMarkupContent(
                        1,
                        $"<iframe class='artifact-frame' src='{url}' title='{System.Net.WebUtility.HtmlEncode(info.Title)}' loading='lazy' referrerpolicy='no-referrer'></iframe>"
                    );
            }
            else
            {
                builder.AddMarkupContent(
                    1,
                    $"<div class='artifact-none'>No inline preview for {System.Net.WebUtility.HtmlEncode(info.ContentType)}.</div>"
                );
            }
        };

    private static bool IsJson(string ct) =>
        ct.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static bool IsCsv(string ct) => ct.Contains("csv", StringComparison.OrdinalIgnoreCase);

    private static string PdfPreviewMarkup(ChatArtifactRenderInfo info, string url)
    {
        var title = System.Net.WebUtility.HtmlEncode(info.Title);
        return $"""
            <div class="artifact-pdf-wrap">
                <div id="chat-artifact-pdf-{info.Id:N}" class="artifact-pdf-mobile"
                     data-pc-pdf data-pdf-url="{url}" aria-label="PDF pages">
                    <div class="pdf-loading">Preparing all PDF pages…</div>
                </div>
                <iframe class="artifact-frame pdf-desktop-frame" src="{url}" title="{title}"
                        loading="lazy" referrerpolicy="no-referrer"></iframe>
            </div>
            """;
    }

    private static bool PreviewableArtifact(string ct) =>
        ct.StartsWith("text/")
        || ct.StartsWith("image/")
        || ct == "application/pdf"
        || ct.Contains("svg")
        || ct.StartsWith("video/")
        || ct.Contains("html");

    private static (
        IReadOnlyList<string> Header,
        IReadOnlyList<IReadOnlyList<string>> Rows
    ) ParseCsv(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines.Length > 0 ? SplitCsvLine(lines[0]) : Array.Empty<string>();
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in lines.Skip(1))
            rows.Add(SplitCsvLine(line));
        return (header, rows);
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    sb.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                cells.Add(sb.ToString());
                sb.Clear();
            }
            else if (c != '\r')
                sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    private static string RenderJson(System.Text.Json.JsonElement el, int depth)
    {
        var sb = new System.Text.StringBuilder();
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                sb.Append(
                    $"<details {(depth < 2 ? "open" : "")} style=\"margin-left:{(depth == 0 ? 0 : 14)}px\"><summary style=\"cursor:pointer; color:var(--text-3)\">{{…}} <span style=\"font-size:10px\">({el.EnumerateObject().Count()})</span></summary>"
                );
                foreach (var p in el.EnumerateObject())
                {
                    sb.Append(
                        $"<div style=\"margin-left:14px\"><span style=\"color:var(--human)\">\"{Esc(p.Name)}\"</span>: "
                    );
                    sb.Append(
                        p.Value.ValueKind
                            is System.Text.Json.JsonValueKind.Object
                                or System.Text.Json.JsonValueKind.Array
                            ? RenderJson(p.Value, depth + 1)
                            : Scalar(p.Value)
                    );
                    sb.Append("</div>");
                }
                sb.Append("</details>");
                break;
            case System.Text.Json.JsonValueKind.Array:
                sb.Append(
                    $"<details {(depth < 2 ? "open" : "")} style=\"margin-left:{(depth == 0 ? 0 : 14)}px\"><summary style=\"cursor:pointer; color:var(--text-3)\">[…] <span style=\"font-size:10px\">({el.GetArrayLength()})</span></summary>"
                );
                var i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    if (i++ >= 200)
                    {
                        sb.Append("<div style=\"margin-left:14px; color:var(--text-3)\">…</div>");
                        break;
                    }
                    sb.Append("<div style=\"margin-left:14px\">");
                    sb.Append(
                        item.ValueKind
                            is System.Text.Json.JsonValueKind.Object
                                or System.Text.Json.JsonValueKind.Array
                            ? RenderJson(item, depth + 1)
                            : Scalar(item)
                    );
                    sb.Append("</div>");
                }
                sb.Append("</details>");
                break;
            default:
                sb.Append(Scalar(el));
                break;
        }
        return sb.ToString();
    }

    private static string Scalar(System.Text.Json.JsonElement v) =>
        v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String =>
                $"<span style=\"color:var(--good)\">\"{Esc(v.GetString() ?? "")}\"</span>",
            System.Text.Json.JsonValueKind.Number =>
                $"<span style=\"color:var(--warn)\">{v.GetRawText()}</span>",
            System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False =>
                $"<span style=\"color:var(--brand-2)\">{v.GetRawText()}</span>",
            _ => "<span style=\"color:var(--text-3)\">null</span>",
        };

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
