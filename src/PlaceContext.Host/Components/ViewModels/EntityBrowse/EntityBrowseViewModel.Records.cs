using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class EntityBrowseViewModel
{
    // ── Records tab ───────────────────────────────────────────────────────────────────────────
    public ProjectTablePageResult? Page { get; private set; }
    public string Search { get; set; } = "";
    public int PageNum { get; private set; } = 1;
    public const int RecordsPageSize = 50;
    public string ViewTab { get; set; } = "records";

    // ── Record detail ─────────────────────────────────────────────────────────────────────────
    public List<(RunArtifactLinkView Art, int Versions)> Artifacts { get; } = new();
    public List<Guid> LinkedRuns { get; } = new();
    public Dictionary<Guid, List<RunArtifactLinkView>> RunArtifacts { get; } = new();

    // ── Records tab ───────────────────────────────────────────────────────────────────────────
    public async Task LoadRecordsPageAsync()
    {
        if (Entity is null) return;
        try
        {
            Page = await _svc.QueryProjectTablePageAsync(ProjectId, Entity.TableName, Search, PageNum, RecordsPageSize);
        }
        catch (Exception ex) { Error = ex.Message; }
        NotifyStateChanged();
    }

    public async Task GoToPageAsync(int page)
    {
        if (Page is null) return;
        var target = Math.Clamp(page, 1, TotalPages(Page));
        if (target == PageNum) return;
        PageNum = target;
        await LoadRecordsPageAsync();
    }

    private CancellationTokenSource? _searchDebounce;

    public async Task OnSearchInputAsync(string value)
    {
        Search = value;
        PageNum = 1;
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounce = cts;
        try { await Task.Delay(300, cts.Token); }
        catch (TaskCanceledException) { return; }
        if (cts.IsCancellationRequested) return;
        await LoadRecordsPageAsync();
        NotifyStateChanged();
    }

    public void CancelSearchDebounce()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
    }

    // ── Label / Record helpers ────────────────────────────────────────────────────────────────
    public string? LabelCol()
    {
        if (Rows is null) return Entity?.LabelColumn;
        var cols = Rows.Columns;
        if (Entity?.LabelColumn is { Length: > 0 } configured
            && cols.FirstOrDefault(c => string.Equals(c, configured, StringComparison.OrdinalIgnoreCase)) is { } exact)
            return exact;
        for (var i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            if (string.Equals(c, "id", StringComparison.OrdinalIgnoreCase)
                || c.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, "ingested_at", StringComparison.OrdinalIgnoreCase)) continue;
            var v = Rows.Rows.FirstOrDefault() is { } first && i < first.Count ? first[i] : null;
            if (v is null || double.TryParse(v, out _)) continue;
            return c;
        }
        return cols.FirstOrDefault();
    }

    public string RecordLabel(IReadOnlyList<string?> row)
    {
        if (Rows is null) return Entity?.Name ?? "";
        var idx = LabelCol() is { } c ? Rows.Columns.ToList().IndexOf(c) : 0;
        return (idx >= 0 && idx < row.Count ? row[idx] : null) ?? Entity?.Name ?? "";
    }

    public static string ComputeRowKey(string table, IReadOnlyList<string?> row, IReadOnlyList<string> colNames,
        IReadOnlyList<DataEntityView> entities, IReadOnlyList<ProjectColumnInfo> columns)
    {
        var colNamesList = colNames.ToList();
        var entity = entities.FirstOrDefault(e => string.Equals(e.TableName, table, StringComparison.OrdinalIgnoreCase));
        if (entity?.LabelColumn is { Length: > 0 } label)
        {
            var li = colNamesList.FindIndex(c => string.Equals(c, label, StringComparison.OrdinalIgnoreCase));
            if (li >= 0 && li < row.Count && !string.IsNullOrEmpty(row[li])) return row[li]!;
        }
        var values = columns
            .Where(c => c.Type is DataColumnTypes.Text or "citext" || c.Type.StartsWith("character", StringComparison.Ordinal) || c.Type.StartsWith("varchar", StringComparison.Ordinal))
            .Select(c => c.Name)
            .Take(3)
            .Select(name => { var i = colNamesList.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i < row.Count ? row[i] : null; })
            .Where(v => !string.IsNullOrEmpty(v));
        return string.Join(" · ", values);
    }

    // ── Record detail ─────────────────────────────────────────────────────────────────────────
    public async Task OpenRecordAsync(IReadOnlyList<string?> row)
    {
        Open = row;
        Related.Clear();
        AutoLinks = Array.Empty<RecordLink>();
        Artifacts.Clear();
        LinkedRuns.Clear();
        RunArtifacts.Clear();
        ShowGraph = false;
        if (Entity is null || Rows is null) return;
        await LoadLinkedArtifactsAsync(row);
        await LoadAutoLinksAsync(row);
        foreach (var rel in Entity.Relations)
        {
            var target = AllEntities.FirstOrDefault(e => string.Equals(e.Name, rel.TargetEntity, StringComparison.OrdinalIgnoreCase));
            var colIdx = Rows.Columns.ToList().FindIndex(c => string.Equals(c, rel.Column, StringComparison.OrdinalIgnoreCase));
            if (target is null || colIdx < 0 || colIdx >= row.Count || row[colIdx] is not { } value) continue;
            try
            {
                var related = await _svc.ExecuteProjectDataAsync(ProjectId,
                    $"SELECT * FROM \"{target.TableName}\" WHERE \"{rel.TargetColumn}\"::text = '{value.Replace("'", "''")}' LIMIT 20");
                Related.Add((rel, related));
            }
            catch { }
        }
        NotifyStateChanged();
    }

    private async Task LoadAutoLinksAsync(IReadOnlyList<string?> row)
    {
        if (Entity is null || Rows is null) return;
        try
        {
            var columns = await _svc.ListProjectTableColumnsAsync(ProjectId, Entity.TableName);
            var rowKey = ComputeRowKey(Entity.TableName, row, Rows.Columns, AllEntities, columns);
            if (string.IsNullOrEmpty(rowKey)) return;
            AutoLinks = await _svc.RelatedRecordLinksAsync(ProjectId, Entity.TableName, rowKey);
        }
        catch { }
    }

    public void CloseRecordPanel()
    {
        Open = null;
        Editing = Creating = false;
        EditError = null;
        WarnMessage = null;
        AutoLinks = Array.Empty<RecordLink>();
        FormValues.Clear();
        KeySnapshot.Clear();
        NotifyStateChanged();
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────────────────────
    public async Task StartEditAsync()
    {
        if (Entity is null) return;
        Creating = false;
        Editing = true;
        EditError = null;
        var columnInfos = (await _svc.ListProjectTableColumnsAsync(ProjectId, Entity.TableName))
            .ToDictionary(c => c.Name, c => c.Type, StringComparer.Ordinal);
        FormColumns = (Rows?.Columns ?? Array.Empty<string>()).Select(c => new FormColumn(c, columnInfos.GetValueOrDefault(c, DataColumnTypes.Text))).ToList();
        FormValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        KeySnapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < FormColumns.Count; i++)
        {
            var v = i < (Open?.Count ?? 0) ? Open![i] : null;
            FormValues[FormColumns[i].Name] = v;
            KeySnapshot[FormColumns[i].Name] = v;
        }
        var label = LabelCol();
        if (label is not null && KeySnapshot.ContainsKey(label))
            KeySnapshot = new Dictionary<string, string?>(StringComparer.Ordinal) { [label] = KeySnapshot[label] };
        else
        {
            var kept = KeySnapshot.Where(kv => !string.IsNullOrEmpty(kv.Value)).Take(3)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (kept.Count > 0) KeySnapshot = kept;
        }
        NotifyStateChanged();
    }

    public async Task StartCreateAsync()
    {
        if (Entity is null) return;
        Creating = true;
        Editing = true;
        Open = null;
        EditError = null;
        var columnInfos = await _svc.ListProjectTableColumnsAsync(ProjectId, Entity.TableName);
        FormColumns = columnInfos.Select(c => new FormColumn(c.Name, c.Type)).ToList();
        if (FormColumns.Count == 0 && Page is { Columns.Count: > 0 })
            FormColumns = Page.Columns.Select(c => new FormColumn(c, DataColumnTypes.Text)).ToList();
        FormValues = FormColumns.ToDictionary(c => c.Name, _ => (string?)null, StringComparer.Ordinal);
        KeySnapshot.Clear();
        NotifyStateChanged();
    }

    public async Task SaveRecordAsync()
    {
        if (Entity is null || FormValues.Count == 0) return;
        Saving = true;
        EditError = null;
        WarnMessage = null;
        try
        {
            if (Creating)
            {
                var created = await _svc.CreateEntityRecordAsync(ProjectId, Entity.TableName, FormValues);
                WarnMessage = created.DuplicateWarnings.Count > 0
                    ? "Created — possible duplicate(s): " + string.Join("; ", created.DuplicateWarnings)
                    : null;
            }
            else
            {
                await _svc.UpdateEntityRecordAsync(ProjectId, Entity.TableName, KeySnapshot, FormValues);
            }
            CloseRecordPanel();
            await ReloadPageAsync();
        }
        catch (Exception ex) { EditError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }

    public async Task DeleteOpenAsync()
    {
        if (Entity is null || Open is null || Rows is null) return;
        await StartEditAsync();
        Editing = false;
        try
        {
            await _svc.DeleteEntityRecordAsync(ProjectId, Entity.TableName, KeySnapshot);
            CloseRecordPanel();
            await ReloadPageAsync();
        }
        catch (Exception ex) { Error = ex.Message; NotifyStateChanged(); }
    }

    public async Task ReloadPageAsync()
    {
        if (Entity is null) return;
        try
        {
            Page = await _svc.QueryProjectTablePageAsync(ProjectId, Entity.TableName, Search, Page?.Page ?? 1, Page?.PageSize ?? 50);
            Rows = await _svc.ExecuteProjectDataAsync(ProjectId, $"SELECT * FROM \"{Entity.TableName}\" LIMIT 500");
        }
        catch (Exception ex) { Error = ex.Message; }
        NotifyStateChanged();
    }

    // ── Record link graph (SVG) ──────────────────────────────────────────────────────────────
    public string GraphSvg()
    {
        var sb = new System.Text.StringBuilder();
        var relNodes = Related
            .SelectMany(r => r.Rows.Rows.Take(3).Select(row => (r.Relation.TargetEntity, Label: row.FirstOrDefault() ?? "—")))
            .Take(6).ToList();
        var right = LinkedRuns.Sum(r => 44 + (RunArtifacts.GetValueOrDefault(r)?.Count ?? 0) * 26);
        var height = Math.Max(160.0, Math.Max(right, relNodes.Count * 44) + 56);
        var cy = height / 2.0;

        sb.Append(FormattableString.Invariant(
            $"<svg viewBox=\"0 0 520 {height}\" style=\"width:100%; border:1px solid var(--border); border-radius:8px; background:var(--card)\">"));

        for (var i = 0; i < relNodes.Count; i++)
        {
            var y = 28 + i * 44;
            sb.Append(FormattableString.Invariant($"<line x1='150' y1='{y}' x2='216' y2='{cy}' stroke='var(--border-2)' stroke-width='1.2'/>"));
            sb.Append(FormattableString.Invariant($"<rect x='8' y='{y - 14}' width='142' height='28' rx='7' fill='var(--human-bg)' stroke='var(--human)'/>"));
            sb.Append(FormattableString.Invariant($"<text x='16' y='{y - 1}' font-size='9.5' fill='var(--human)' font-family='JetBrains Mono, monospace'>{E(relNodes[i].TargetEntity)}</text>"));
            sb.Append(FormattableString.Invariant($"<text x='16' y='{y + 10}' font-size='9' fill='var(--text-2)'>{E(FormatHelper.Trunc(relNodes[i].Label, 22))}</text>"));
        }

        sb.Append(FormattableString.Invariant($"<rect x='216' y='{cy - 17}' width='120' height='34' rx='8' fill='var(--brand-bg)' stroke='var(--brand-2)' stroke-width='1.4'/>"));
        sb.Append(FormattableString.Invariant($"<text x='276' y='{cy - 2}' font-size='10' font-weight='600' fill='var(--text)' text-anchor='middle'>{E(FormatHelper.Trunc(Open is null ? "" : RecordLabel(Open), 18))}</text>"));
        sb.Append(FormattableString.Invariant($"<text x='276' y='{cy + 10}' font-size='8.5' fill='var(--text-3)' text-anchor='middle'>{E(Entity?.Name ?? "")} record</text>"));

        var yCursor = 28.0;
        foreach (var runId in LinkedRuns)
        {
            var arts = RunArtifacts.GetValueOrDefault(runId) ?? new List<RunArtifactLinkView>();
            var y = yCursor;
            sb.Append(FormattableString.Invariant($"<line x1='336' y1='{cy}' x2='376' y2='{y}' stroke='var(--border-2)' stroke-width='1.2' stroke-dasharray='4 3'/>"));
            sb.Append($"<a href='/observability?run={runId}'>");
            sb.Append(FormattableString.Invariant($"<rect x='376' y='{y - 13}' width='132' height='26' rx='7' fill='var(--card-2)' stroke='var(--border-2)' style='cursor:pointer'/>"));
            sb.Append(FormattableString.Invariant($"<text x='384' y='{y + 3}' font-size='9' fill='var(--text-2)' font-family='JetBrains Mono, monospace' style='cursor:pointer'>run {runId.ToString("N")[..8]} ↗</text>"));
            sb.Append("</a>");
            for (var k = 0; k < arts.Count; k++)
            {
                var ay = y + 26 + k * 26;
                sb.Append(FormattableString.Invariant($"<line x1='442' y1='{y + 13}' x2='442' y2='{ay - 11}' stroke='var(--border-2)'/>"));
                sb.Append($"<a href='/artifacts?artifact={arts[k].Id}'>");
                sb.Append(FormattableString.Invariant($"<rect x='396' y='{ay - 11}' width='112' height='22' rx='6' fill='var(--good-bg)' stroke='var(--good)' style='cursor:pointer'/>"));
                sb.Append(FormattableString.Invariant($"<text x='403' y='{ay + 3}' font-size='8.5' fill='var(--good)' font-family='JetBrains Mono, monospace' style='cursor:pointer'>{E(FormatHelper.Trunc(arts[k].Title, 17))} ↗</text>"));
                sb.Append("</a>");
            }
            yCursor += 44 + arts.Count * 26;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string E(string s) => FormatHelper.HtmlEscape(s);
}
