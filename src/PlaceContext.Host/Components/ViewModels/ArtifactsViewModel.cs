using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ArtifactsViewModel : PageViewModel, IDisposable
{
    private readonly PlaceContextService Svc;
    private readonly PortalUiState Ui;
    private readonly NavigationManager Nav;
    private readonly IJSRuntime Js;
    private readonly IObjectStore Store;
    private readonly IArtifactViewConfigService ArtifactConfigService;
    private readonly IRunArtifactLinkRepository Links;
    private readonly IPermissionService Permissions;

    public ArtifactsViewModel(
        PlaceContextService svc,
        PortalUiState ui,
        NavigationManager nav,
        IJSRuntime js,
        IObjectStore store,
        IArtifactViewConfigService artifactConfig,
        IRunArtifactLinkRepository links,
        IPermissionService permissions
    )
    {
        Svc = svc;
        Ui = ui;
        Nav = nav;
        Js = js;
        Store = store;
        ArtifactConfigService = artifactConfig;
        Links = links;
        Permissions = permissions;
    }

    [SupplyParameterFromQuery(Name = "artifact")]
    public string? ArtifactId { get; set; }

    public IReadOnlyList<ArtifactFileView> Files = Array.Empty<ArtifactFileView>();
    public IReadOnlyList<ProjectSummaryView> Projects = Array.Empty<ProjectSummaryView>();
    public ArtifactFileView? Active;
    public string Filter = "";
    public string ProjectFilter = "";
    public string? CategoryFilter;
    public PlaceContext.Application.Ports.ArtifactViewConfig ArtifactConfig = new([]);
    public System.Text.Json.JsonElement? Content;
    public string? RawText;
    public (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows)? Csv;
    public bool Truncated;
    public bool Loading;
    public int OpenGeneration;
    public bool LoadFailed;
    public string? LoadError;
    public bool ConfirmDelete;
    public bool Deleting;
    public readonly HashSet<Guid> Selected = new();
    public bool BulkDeleting;

    // UI-level hiding only — the actual DeleteArtifact(s) commands are also gated server-side (the
    // dispatcher rejects them for a caller lacking artifacts.delete regardless of what the UI shows).
    public bool CanDelete;
    public bool CanShare;
    public bool CanManageSettings;
    public bool ShareOpen;
    public bool ShareBusy;
    public int ShareLifetimeDays = 7;
    public ArtifactShareStatus? ShareStatus;
    public string? ShareUrl;
    public string? ShareMessage;
    public DateTimeOffset? ShareCreatedExpiresAt;

    public const int MaxInlineBytes = 2 * 1024 * 1024;
    public const int MaxCsvRows = 500;
    public const string OtherCategory = "__other";

    // ── Pagination over the grouped (version-stacked) file list ─────────────────────────────────
    public const int FilesPageSize = 25;
    public int FilesPage = 1;
    public CancellationTokenSource? SearchDebounce;

    // The per-project/recent load is capped — this flags when the returned count hit that cap, so
    // the UI can say "there may be more" instead of silently acting as if the list is complete.
    public const int ProjectArtifactsCap = 2000;
    public const int RecentArtifactsCap = 1000;
    public bool LoadMayBeIncomplete;

    public async Task AttachAndInitializeAsync(Func<Task> stateChanged)
    {
        Attach(stateChanged);
        Ui.Set("Artifacts", "every file your runs produced — click to view");
        CanDelete = await Permissions.HasAsync(Permission.ArtifactsDelete);
        CanShare = await Permissions.HasAsync(Permission.ArtifactsShare);
        CanManageSettings = await Permissions.HasAsync(Permission.SettingsManage);
        try
        {
            ArtifactConfig = await ArtifactConfigService.GetAsync();
        }
        catch { }
        try
        {
            Projects = await Svc.GetProjectsAsync();
        }
        catch { }
        // Default the filter to the project the sidebar is on; a project-scoped load pulls ALL of
        // that project's artifacts, so older files aren't hidden behind a global "recent" cap.
        if (Ui.CurrentProjectId is { } pid)
            ProjectFilter = pid.ToString();
        await LoadFilesAsync();

        await ApplyDeepLinkAsync();
    }

    public async Task AfterRenderAsync(bool firstRender)
    {
        if (Active is null || !IsPdf(Active.ContentType))
            return;

        try
        {
            await Js.InvokeVoidAsync("placecontext.renderPdf", PdfMobileId(Active), Url(Active));
        }
        catch
        {
            // The native desktop frame and Open link remain available if client rendering fails.
        }
    }

    // Loads the file list for the current filter: project-scoped (complete, and search-widened —
    // the ILIKE runs server-side on Title/Kind) when a project is selected, else the recent feed
    // across all projects (client-filtered only, since the recent feed has no project to scope to).
    public async Task LoadFilesAsync()
    {
        try
        {
            if (Guid.TryParse(ProjectFilter, out var pid))
            {
                var search = string.IsNullOrWhiteSpace(Filter) ? null : Filter;
                Files = await Svc.ListProjectArtifactsAsync(pid, ProjectArtifactsCap, search);
                LoadMayBeIncomplete = Files.Count >= ProjectArtifactsCap;
            }
            else
            {
                Files = await Svc.ListRecentArtifactsAsync(RecentArtifactsCap);
                LoadMayBeIncomplete = Files.Count >= RecentArtifactsCap;
            }
        }
        catch
        {
            Files = Array.Empty<ArtifactFileView>();
            LoadMayBeIncomplete = false;
        }
    }

    // Supplied from the query: re-fires on same-page navigation (search hits, graph links).
    public async Task ApplyDeepLinkAsync() => await ApplyDeepLinkCoreAsync();

    private async Task ApplyDeepLinkCoreAsync()
    {
        if (!Guid.TryParse(ArtifactId, out var artifactId))
            return;
        if (Active?.Id == artifactId)
            return;
        var target = Files.FirstOrDefault(f => f.Id == artifactId);
        if (target is null)
        {
            // The deep-linked artifact may belong to a different project than the current filter —
            // fall back to the recent global feed to locate it.
            try
            {
                Files = await Svc.ListRecentArtifactsAsync(RecentArtifactsCap);
            }
            catch { }
            target = Files.FirstOrDefault(f => f.Id == artifactId);
        }
        if (target is null)
        {
            // Still not found — the artifact may be older than both the project-scoped and recent
            // caps (e.g. linked directly from an entity row via EntityBrowse). Rather than give up,
            // resolve it directly by id through the same tenant-scoped repository ReadTextAsync uses.
            LoadError = null;
            try
            {
                var link = await Links.GetByIdAsync(artifactId);
                if (link is null)
                {
                    LoadError = "artifact not found for this workspace";
                    return;
                }
                target = new ArtifactFileView(
                    link.Id,
                    link.RunId,
                    link.JobId,
                    link.ProjectId,
                    link.Kind.ToString(),
                    link.Title,
                    link.ContentType,
                    link.SizeBytes,
                    link.CreatedAt
                );
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                return;
            }
        }
        if (target is { })
        {
            ProjectFilter = target.ProjectId.ToString(); // make sure it's visible in the list
            await LoadFilesAsync();
            target = Files.FirstOrDefault(f => f.Id == artifactId) ?? target;
            await OpenAsync(target);
        }
    }

    public async Task OnProjectFilter(ChangeEventArgs e)
    {
        ProjectFilter = e.Value?.ToString() ?? "";
        Active = null;
        ConfirmDelete = false;
        ResetShareUi();
        Selected.Clear();
        FilesPage = 1;
        await LoadFilesAsync();
    }

    // Debounced so a search widens the server-side query only once typing pauses (~300ms), not on
    // every keystroke; the client-side group+paginate step re-runs immediately for instant feedback
    // on the "All projects" feed, which is filtered client-side only.
    public async Task OnFilterInputAsync(ChangeEventArgs e)
    {
        Filter = e.Value?.ToString() ?? "";
        FilesPage = 1;
        SearchDebounce?.Cancel();
        SearchDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        SearchDebounce = cts;
        try
        {
            await Task.Delay(300, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested)
            return;
        await LoadFilesAsync();
        NotifyStateChanged();
    }

    public void GoToFilesPage(int page) => FilesPage = Math.Max(1, page);

    public void SetCategoryFilter(string? category)
    {
        CategoryFilter = category;
        FilesPage = 1;
        Expanded = null;
        Selected.Clear();
    }

    public void Dispose()
    {
        SearchDebounce?.Cancel();
        SearchDebounce?.Dispose();
    }

    // ── Multi-select for bulk delete ────────────────────────────────────────────────────────────
    public void ToggleOne(Guid id, bool on)
    {
        if (on)
            Selected.Add(id);
        else
            Selected.Remove(id);
    }

    public void ToggleGroup(IReadOnlyList<ArtifactFileView> group, bool on)
    {
        foreach (var v in group)
        {
            if (on)
                Selected.Add(v.Id);
            else
                Selected.Remove(v.Id);
        }
    }

    public bool GroupAllSelected(IReadOnlyList<ArtifactFileView> group) =>
        group.Count > 0 && group.All(v => Selected.Contains(v.Id));

    public async Task DeleteSelectedAsync()
    {
        if (Selected.Count == 0 || BulkDeleting)
            return;
        BulkDeleting = true;
        var ids = Selected.ToList();
        try
        {
            await Svc.DeleteArtifactsAsync(ids);
        }
        catch { }
        finally
        {
            BulkDeleting = false;
        }
        var idSet = ids.ToHashSet();
        Files = Files.Where(f => !idSet.Contains(f.Id)).ToList();
        Selected.Clear();
        if (Active is { } a && idSet.Contains(a.Id))
        {
            Active = null;
            ResetShareUi();
            Nav.NavigateTo("/artifacts", replace: true);
        }
    }

    public string ProjectName(Guid id) => Projects.FirstOrDefault(p => p.Id == id)?.Name ?? "—";

    public string? Expanded;

    public static string GroupKey(ArtifactFileView a) => $"{a.ProjectId}|{a.Kind}|{a.Title}";

    // Versioning: the same logical file (project + kind + title) groups newest-first; the list
    // shows the latest with the history behind the vN dropdown.
    public IReadOnlyList<IReadOnlyList<ArtifactFileView>> Grouped() =>
        Filtered()
            .GroupBy(GroupKey)
            .Select(g =>
                (IReadOnlyList<ArtifactFileView>)g.OrderByDescending(a => a.CreatedAt).ToList()
            )
            .OrderByDescending(g => g[0].CreatedAt)
            .ToList();

    public IReadOnlyList<ArtifactFileView> Filtered()
    {
        IEnumerable<ArtifactFileView> q = BaseFiltered();
        if (CategoryFilter == OtherCategory)
            q = q.Where(file => ArtifactConfig.CategoryFor(file.Title) is null);
        else if (!string.IsNullOrWhiteSpace(CategoryFilter))
            q = q.Where(file =>
                string.Equals(
                    ArtifactConfig.CategoryFor(file.Title),
                    CategoryFilter,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        return q.ToList();
    }

    public IReadOnlyList<ArtifactFileView> BaseFiltered()
    {
        IEnumerable<ArtifactFileView> q = Files;
        if (Guid.TryParse(ProjectFilter, out var pid))
            q = q.Where(f => f.ProjectId == pid);
        if (!string.IsNullOrWhiteSpace(Filter))
            q = q.Where(f =>
                f.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || f.Kind.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            );
        return q.ToList();
    }

    public int CategoryCount(string categoryId) =>
        BaseFiltered()
            .Count(file =>
                string.Equals(
                    ArtifactConfig.CategoryFor(file.Title),
                    categoryId,
                    StringComparison.OrdinalIgnoreCase
                )
            );

    public int OtherCount() =>
        BaseFiltered().Count(file => ArtifactConfig.CategoryFor(file.Title) is null);

    public string EmptyMessage()
    {
        if (!string.IsNullOrWhiteSpace(Filter))
            return "No files match your search and selected filters.";
        if (CategoryFilter == OtherCategory)
            return "No uncategorized artifact files match the selected project.";
        if (!string.IsNullOrWhiteSpace(CategoryFilter))
        {
            var label =
                ArtifactConfig
                    .Categories.FirstOrDefault(category => category.Id == CategoryFilter)
                    ?.Label
                ?? "this category";
            return $"No {label} files match the selected project.";
        }
        return "No stored artifacts here yet — every completed run generates one from its job's return type.";
    }

    public async Task OpenAsync(ArtifactFileView a)
    {
        var openGeneration = ++OpenGeneration;
        Active = a;
        // Keep the URL shareable: every opened file lands in ?artifact= without growing history.
        // Replacing the browser URL directly avoids re-running the page's route lifecycle while
        // the viewer is opening (which otherwise causes a visible flash and can race this load).
        try
        {
            await Js.InvokeVoidAsync(
                "history.replaceState",
                null,
                "",
                $"/artifacts?artifact={a.Id}"
            );
        }
        catch (JSDisconnectedException) { }
        Content = null;
        Csv = null;
        RawText = null;
        Truncated = false;
        LoadFailed = false;
        ConfirmDelete = false;
        ResetShareUi();
        if (!IsJson(a.ContentType) && !IsCsv(a.ContentType))
            return;

        Loading = true;
        try
        {
            var text = await ReadTextAsync(a);
            if (openGeneration != OpenGeneration)
                return;
            if (text is null)
            {
                LoadFailed = true;
                LoadError ??= "the object store returned nothing for this file";
                return;
            }
            RawText = text;
            if (IsJson(a.ContentType))
            {
                // Not all *.json artifacts hold valid JSON (raw bundles keep whatever the run
                // printed) — parse failure falls back to the raw text, not an error.
                try
                {
                    Content = System.Text.Json.JsonDocument.Parse(text).RootElement.Clone();
                }
                catch
                {
                    Content = null;
                }
            }
            else
            {
                Csv = ParseCsv(text);
            }
        }
        finally
        {
            if (openGeneration == OpenGeneration)
                Loading = false;
        }
    }

    // Server-side fetch through the tenant-scoped link → object store (same objects the stream
    // endpoint serves), capped so a huge file can't balloon the circuit.
    public async Task<string?> ReadTextAsync(ArtifactFileView a)
    {
        LoadError = null;
        try
        {
            var link = await Links.GetByIdAsync(a.Id);
            if (link is null)
            {
                LoadError = "artifact link not found for this workspace";
                return null;
            }
            var obj = await Store.OpenReadAsync(link.Bucket, link.ObjectKey);
            if (obj is null)
            {
                LoadError = "object missing from the store";
                return null;
            }
            await using var stream = obj.Content;
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read,
                total = 0;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > MaxInlineBytes)
                {
                    Truncated = true;
                    break;
                }
                ms.Write(buffer, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            return null;
        }
    }

    // ── JSON tree viewer ──────────────────────────────────────────────────────────────────────────
    public RenderFragment JsonTree(System.Text.Json.JsonElement? el) =>
        builder => builder.AddMarkupContent(0, RenderJson(el!.Value, 0));

    public static string RenderJson(System.Text.Json.JsonElement el, int depth)
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

    public static string Scalar(System.Text.Json.JsonElement v) =>
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

    public static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── CSV table ─────────────────────────────────────────────────────────────────────────────────
    public (IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>) ParseCsv(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines.Length > 0 ? SplitCsvLine(lines[0]) : Array.Empty<string>();
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in lines.Skip(1))
        {
            if (rows.Count >= MaxCsvRows)
            {
                Truncated = true;
                break;
            }
            rows.Add(SplitCsvLine(line));
        }
        return (header, rows);
    }

    public static IReadOnlyList<string> SplitCsvLine(string line)
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

    public async Task DeleteActiveAsync()
    {
        if (Active is null || Deleting)
            return;
        var id = Active.Id;
        Deleting = true;
        try
        {
            await Svc.DeleteArtifactAsync(id);
        }
        catch { }
        finally
        {
            Deleting = false;
        }
        Files = Files.Where(f => f.Id != id).ToList();
        ++OpenGeneration;
        Active = null;
        ConfirmDelete = false;
        ResetShareUi();
        // Drop the ?artifact= deep link so a refresh doesn't try to reopen the deleted file.
        Nav.NavigateTo("/artifacts", replace: true);
    }

    // Always path-absolute on the current host — never bake Request.Host (breaks when the
    // reverse-proxy public DNS differs from the in-cluster name).
    public static string Url(ArtifactFileView a) =>
        $"/runs/{a.RunId}/artifacts/{a.Id}?v={a.CreatedAt.ToUnixTimeSeconds()}";

    public void CloseViewer()
    {
        ++OpenGeneration;
        Active = null;
        ResetShareUi();
        Nav.NavigateTo("/artifacts", replace: true);
    }

    public async Task ToggleShareAsync()
    {
        if (Active is null || ShareBusy)
            return;
        if (ShareOpen)
        {
            ResetShareUi();
            return;
        }

        ShareOpen = true;
        ShareBusy = true;
        ShareMessage = null;
        try
        {
            ShareStatus = await Svc.GetArtifactShareStatusAsync(Active.Id);
        }
        catch
        {
            ShareMessage = "Couldn't load this artifact's share status.";
        }
        finally
        {
            ShareBusy = false;
        }
    }

    public async Task CreateShareAsync()
    {
        if (Active is null || ShareBusy)
            return;
        ShareBusy = true;
        ShareMessage = null;
        try
        {
            var created = await Svc.CreateArtifactShareAsync(Active.Id, ShareLifetimeDays);
            ShareUrl = Nav.ToAbsoluteUri($"/share/artifacts/{created.Token}").AbsoluteUri;
            ShareCreatedExpiresAt = created.ExpiresAt;
            ShareStatus = new ArtifactShareStatus(
                true,
                created.TokenPrefix,
                DateTimeOffset.UtcNow,
                created.ExpiresAt,
                null
            );
        }
        catch
        {
            ShareMessage = "Couldn't create a public link.";
        }
        finally
        {
            ShareBusy = false;
        }
    }

    public async Task RevokeShareAsync()
    {
        if (Active is null || ShareBusy)
            return;
        ShareBusy = true;
        ShareMessage = null;
        try
        {
            await Svc.RevokeArtifactShareAsync(Active.Id);
            ShareStatus = null;
            ShareUrl = null;
            ShareCreatedExpiresAt = null;
            ShareMessage = "Public access revoked.";
        }
        catch
        {
            ShareMessage = "Couldn't revoke the public link.";
        }
        finally
        {
            ShareBusy = false;
        }
    }

    public async Task CopyShareAsync()
    {
        if (ShareUrl is null)
            return;
        try
        {
            await Js.InvokeVoidAsync("navigator.clipboard.writeText", ShareUrl);
            ShareMessage = "Link copied.";
        }
        catch
        {
            ShareMessage = "Copy failed — select and copy the link manually.";
        }
    }

    public void ResetShareUi()
    {
        ShareOpen = false;
        ShareBusy = false;
        ShareStatus = null;
        ShareUrl = null;
        ShareMessage = null;
        ShareCreatedExpiresAt = null;
    }

    public static bool IsJson(string ct) => ct.Contains("json", StringComparison.OrdinalIgnoreCase);

    public static bool IsCsv(string ct) => ct.Contains("csv", StringComparison.OrdinalIgnoreCase);

    public static bool IsPdf(string ct) =>
        ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public static string PdfMobileId(ArtifactFileView artifact) => $"artifact-pdf-{artifact.Id:N}";

    public static bool IsImage(ArtifactFileView artifact)
    {
        if (artifact.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        var extension = Path.GetExtension(artifact.Title);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Previewable(string ct) =>
        ct.StartsWith("text/")
        || ct.StartsWith("image/")
        || ct == "application/pdf"
        || ct.Contains("svg")
        || ct.StartsWith("video/");

    // Stroke-style file icons matching the sidebar's icon set — no emoji.
    public static RenderFragment FileIcon(string ct, int size) =>
        builder =>
        {
            var inner = ct switch
            {
                var c when c.StartsWith("image/") =>
                    "<rect x='3' y='4' width='18' height='16' rx='2'></rect><circle cx='8.5' cy='9.5' r='1.7'></circle><path d='M21 15.5l-4.5-4.5L6 21.5'></path>",
                "application/pdf" =>
                    "<path d='M6 2h8l5 5v15H6V2Z'></path><path d='M14 2v5h5'></path><path d='M9 13.5h6M9 17h6'></path>",
                var c when c.StartsWith("video/") =>
                    "<rect x='2.5' y='5' width='13' height='14' rx='2'></rect><path d='M15.5 10l6-3.5v11l-6-3.5'></path>",
                var c when c.Contains("html") =>
                    "<circle cx='12' cy='12' r='9'></circle><path d='M3 12h18M12 3c2.8 2.6 4 5.7 4 9s-1.2 6.4-4 9c-2.8-2.6-4-5.7-4-9s1.2-6.4 4-9Z'></path>",
                var c when c.Contains("csv") =>
                    "<rect x='3' y='4' width='18' height='16' rx='2'></rect><path d='M3 9.5h18M3 15h18M9 4v16M15 4v16'></path>",
                var c when c.Contains("json") =>
                    "<path d='M8 3c-2 0-3 1-3 3v3c0 1.4-.8 2.3-2 3 1.2.7 2 1.6 2 3v3c0 2 1 3 3 3'></path><path d='M16 3c2 0 3 1 3 3v3c0 1.4.8 2.3 2 3-1.2.7-2 1.6-2 3v3c0 2-1 3-3 3'></path>",
                _ => "<path d='M6 2h8l5 5v15H6V2Z'></path><path d='M14 2v5h5'></path>",
            };
            builder.AddMarkupContent(
                0,
                $"<svg width='{size}' height='{size}' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'>{inner}</svg>"
            );
        };

    public static string FormatBytes(long n) =>
        n >= 1_048_576 ? $"{n / 1_048_576.0:0.#} MB"
        : n >= 1024 ? $"{n / 1024.0:0.#} KB"
        : $"{n} B";
}
