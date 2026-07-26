using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class EntityBrowseViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;

    public EntityBrowseViewModel(IPlaceContextService svc) => _svc = svc;

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }
    public string EntityName { get; set; } = "";
    public string? RecordKey { get; set; }

    // ── Entity state ──────────────────────────────────────────────────────────────────────────
    public DataEntityView? Entity { get; private set; }
    public IReadOnlyList<DataEntityView> AllEntities { get; private set; } = Array.Empty<DataEntityView>();
    public ProjectQueryResult? Rows { get; private set; }
    public IReadOnlyList<string?>? Open { get; private set; }
    public List<(EntityRelationDto Relation, ProjectQueryResult Rows)> Related { get; } = new();
    public IReadOnlyList<RecordLink> AutoLinks { get; private set; } = Array.Empty<RecordLink>();
    public string? Error { get; private set; }
    public string? WarnMessage { get; private set; }
    public bool Loaded { get; private set; }

    // ── Edit state ────────────────────────────────────────────────────────────────────────────
    public bool Editing { get; private set; }
    public bool Creating { get; private set; }
    public bool Saving { get; private set; }
    public string? EditError { get; private set; }
    public record FormColumn(string Name, string Type);
    public List<FormColumn> FormColumns { get; private set; } = new();
    public Dictionary<string, string?> FormValues { get; private set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string?> KeySnapshot { get; private set; } = new(StringComparer.Ordinal);

    // ── Records tab ───────────────────────────────────────────────────────────────────────────
    public ProjectTablePageResult? Page { get; private set; }
    public string Search { get; set; } = "";
    public int PageNum { get; private set; } = 1;
    public const int RecordsPageSize = 50;
    public string ViewTab { get; set; } = "records";

    // ── Charts ────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<ProjectChartView> Charts { get; private set; } = Array.Empty<ProjectChartView>();
    public bool ShowChartEditor { get; private set; }
    public bool ChartBusy { get; private set; }
    public string ChartName { get; set; } = "";
    public string ChartSql { get; set; } = "";
    public string ChartType { get; set; } = "bar";
    public string? ChartError { get; private set; }
    public bool ChartMonaco { get; set; } = true;
    public bool ChartMonacoReady { get; set; }
    public const string ChartEditorId = "pcmonaco-entchart";
    public Dictionary<string, DateTimeOffset> RenderedCharts { get; } = new();

    // ── Graph ─────────────────────────────────────────────────────────────────────────────────
    public GraphVizView? BrainGraph { get; private set; }
    public Dictionary<string, string> NodeUrls { get; } = new();
    public string? FocusKey { get; set; }
    public bool ShowGraph { get; set; }

    // ── Record detail ─────────────────────────────────────────────────────────────────────────
    public List<(RunArtifactLinkView Art, int Versions)> Artifacts { get; } = new();
    public List<Guid> LinkedRuns { get; } = new();
    public Dictionary<Guid, List<RunArtifactLinkView>> RunArtifacts { get; } = new();

    // ── Insights ──────────────────────────────────────────────────────────────────────────────
    public sealed record Insight(string Title, string? Big, string? Sub,
        IReadOnlyList<(string Label, string Count, int Frac)> Bars);
    public List<Insight> Insights { get; } = new();

    // ── Computed ──────────────────────────────────────────────────────────────────────────────
    public string ChartPrefix => $"sql:{Entity?.Name} · ";
    private bool _defaultsTried;

    public IReadOnlyList<ProjectChartView> EntityCharts()
        => Charts.Where(c => c.TableName.StartsWith(ChartPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

    public string ChartShortName(ProjectChartView c) => c.TableName[ChartPrefix.Length..];

    public static string ChartCanvasId(string slot)
        => "pcentchart-" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
               System.Text.Encoding.UTF8.GetBytes(slot)))[..12];

    public static string? StoredSql(ProjectChartView chart)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(chart.Html)?["sql"]?.GetValue<string>(); }
        catch { return null; }
    }

    public static string StoredType(ProjectChartView chart)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(chart.Html)?["type"]?.GetValue<string>() ?? "bar"; }
        catch { return "bar"; }
    }

    public static int TotalPages(ProjectTablePageResult page)
        => Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)Math.Max(1, page.PageSize)));

    public string? NodeUrl(GraphNodeView node) => NodeUrls.GetValueOrDefault(node.Id);

    public static bool IsNumeric(string type) =>
        type.Contains("int") || type.Contains("numeric") || type.Contains("double") || type.Contains("real");

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        NodeUrls.Clear();
    }

    public async Task LoadAsync()
    {
        Error = null;
        Open = null;
        Loaded = false;
        try
        {
            AllEntities = await _svc.ListDataEntitiesAsync(ProjectId);
            Entity = AllEntities.FirstOrDefault(e => string.Equals(e.Name, EntityName, StringComparison.OrdinalIgnoreCase));
            if (Entity is null) return;
            Rows = await _svc.ExecuteProjectDataAsync(ProjectId, $"SELECT * FROM \"{Entity.TableName}\" LIMIT 500");
            try { Charts = await _svc.ListProjectChartsAsync(ProjectId); } catch { }

            FocusKey = null;
            if (RecordKey is { Length: > 0 } recordKey
                && Rows is not null
                && Rows.Rows.Any(r => r.Any(v => string.Equals(v, recordKey, StringComparison.OrdinalIgnoreCase))))
            {
                FocusKey = recordKey;
                ViewTab = "graph";
            }
            await BuildInsightsAsync();
            await BuildSectionGraphAsync();

            Search = "";
            PageNum = 1;
            await LoadRecordsPageAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loaded = true; NotifyStateChanged(); }
    }

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
            .Where(c => c.Type is "text" or "citext" || c.Type.StartsWith("character", StringComparison.Ordinal) || c.Type.StartsWith("varchar", StringComparison.Ordinal))
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
        FormColumns = (Rows?.Columns ?? Array.Empty<string>()).Select(c => new FormColumn(c, columnInfos.GetValueOrDefault(c, "text"))).ToList();
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
            FormColumns = Page.Columns.Select(c => new FormColumn(c, "text")).ToList();
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

    // ── Artifacts ─────────────────────────────────────────────────────────────────────────────
    private async Task LoadLinkedArtifactsAsync(IReadOnlyList<string?> row)
    {
        if (Entity is null || Rows is null) return;
        var cols = Rows.Columns.ToList();
        var keys = new List<string>();
        if (LabelCol() is { } label)
        {
            var li = cols.FindIndex(c => string.Equals(c, label, StringComparison.OrdinalIgnoreCase));
            if (li >= 0 && li < row.Count && row[li] is { Length: > 2 } lv) keys.Add(lv);
        }
        foreach (var rel in Entity.Relations)
        {
            var ci = cols.FindIndex(c => string.Equals(c, rel.Column, StringComparison.OrdinalIgnoreCase));
            if (ci >= 0 && ci < row.Count && row[ci] is { Length: > 2 } cv) keys.Add(cv);
        }

        var runIds = new HashSet<Guid>();
        foreach (var key in keys.Distinct().Take(3))
        {
            try { foreach (var id in await _svc.ListTaggedRunsAsync(Entity.Id, key)) runIds.Add(id); }
            catch { }
        }

        foreach (var key in runIds.Count > 0 ? Enumerable.Empty<string>() : keys.Distinct().Take(3))
        {
            try
            {
                var hits = await _svc.ExecuteProjectDataAsync(ProjectId,
                    $"SELECT DISTINCT run_id::text FROM job_run_data WHERE artifact ILIKE '%{key.Replace("'", "''")}%' LIMIT 10");
                foreach (var r in hits.Rows)
                    if (r.Count > 0 && Guid.TryParse(r[0], out var id)) runIds.Add(id);
            }
            catch { }
        }

        var collected = new List<RunArtifactLinkView>();
        foreach (var runId in runIds.Take(8))
        {
            LinkedRuns.Add(runId);
            try
            {
                var arts = (await _svc.ListRunArtifactsAsync(runId)).ToList();
                RunArtifacts[runId] = arts;
                collected.AddRange(arts);
            }
            catch { RunArtifacts[runId] = new List<RunArtifactLinkView>(); }
        }
        Artifacts.AddRange(collected
            .GroupBy(a => (a.Kind, a.Title))
            .Select(g => (g.OrderByDescending(a => a.CreatedAt).First(), g.Count()))
            .OrderByDescending(x => x.Item1.CreatedAt));
    }

    // ── Insights ──────────────────────────────────────────────────────────────────────────────
    public async Task BuildInsightsAsync()
    {
        Insights.Clear();
        if (Entity is null) return;
        var table = Entity.TableName.Replace("\"", "");
        try
        {
            var columns = await _svc.ListProjectTableColumnsAsync(ProjectId, Entity.TableName);
            Insights.Add(new Insight($"{Entity.Name} total",
                (Rows?.Rows.Count ?? 0).ToString("N0"), "records", Array.Empty<(string, string, int)>()));

            foreach (var col in columns.Where(c => IsNumeric(c.Type)).Take(3))
            {
                var r = await _svc.ExecuteProjectDataAsync(ProjectId,
                    $"SELECT round(avg(\"{col.Name}\")::numeric, 1), min(\"{col.Name}\"), max(\"{col.Name}\") FROM \"{table}\"");
                if (r.Rows.Count == 1 && r.Rows[0][0] is { } avg)
                    Insights.Add(new Insight($"avg {col.Name}", avg,
                        $"range {r.Rows[0][1]} – {r.Rows[0][2]}", Array.Empty<(string, string, int)>()));
            }

            var keyCols = Entity.Relations.Select(rel => rel.Column)
                .Concat(columns.Where(c => c.Type.Contains("text") || c.Type.Contains("char")).Select(c => c.Name))
                .Where(c => !string.Equals(c, Entity.LabelColumn, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2);
            foreach (var col in keyCols)
            {
                var r = await _svc.ExecuteProjectDataAsync(ProjectId,
                    $"SELECT \"{col}\"::text, count(*) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 5");
                if (r.Rows.Count == 0) continue;
                var max = r.Rows.Max(x => long.TryParse(x[1], out var n) ? n : 0);
                var bars = r.Rows
                    .Select(x => (x[0] ?? "—", x[1] ?? "0",
                        max > 0 && long.TryParse(x[1], out var n) ? (int)(n * 100 / max) : 0))
                    .ToList();
                Insights.Add(new Insight($"by {col}", null, null, bars));
            }
        }
        catch { }
        NotifyStateChanged();
    }

    // ── Charts ────────────────────────────────────────────────────────────────────────────────
    public async Task OpenAnalyticsAsync()
    {
        ViewTab = "analytics";
        RenderedCharts.Clear();
        ChartMonacoReady = false;
        await EnsureDefaultChartsAsync();
        NotifyStateChanged();
    }

    private async Task EnsureDefaultChartsAsync()
    {
        if (_defaultsTried || Entity is null || EntityCharts().Count > 0) return;
        _defaultsTried = true;
        try
        {
            var table = Entity.TableName.Replace("\"", "");
            var columns = await _svc.ListProjectTableColumnsAsync(ProjectId, Entity.TableName);
            var categorical = Entity.Relations.Select(r => r.Column)
                .Concat(columns.Where(c => (c.Type.Contains("text") || c.Type.Contains("char"))
                    && !string.Equals(c.Name, Entity.LabelColumn, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var numeric = columns.Where(c => (c.Type.Contains("int") || c.Type.Contains("numeric") || c.Type.Contains("double") || c.Type.Contains("real"))
                    && !string.Equals(c.Name, "id", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name).ToList();

            async Task Seed(string name, string sql, string type)
            {
                try { await _svc.SaveSqlChartAsync(ProjectId, $"{Entity.Name} · {name}", sql, type); } catch { }
            }

            if (categorical.FirstOrDefault() is { } cat)
            {
                await Seed($"records by {cat}",
                    $"SELECT \"{cat}\"::text, count(*) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 10", "bar");
                if (numeric.FirstOrDefault() is { } num)
                    await Seed($"avg {num} by {cat}",
                        $"SELECT \"{cat}\"::text, round(avg(\"{num}\")::numeric, 1) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 10", "bar");
                if (categorical.Skip(1).FirstOrDefault() is { } cat2)
                    await Seed($"share by {cat2}",
                        $"SELECT \"{cat2}\"::text, count(*) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 8", "pie");
            }
            else if (numeric.FirstOrDefault() is { } num && LabelCol() is { } label)
            {
                await Seed($"{num} by {label}",
                    $"SELECT \"{label.Replace("\"", "")}\"::text, \"{num}\" FROM \"{table}\" ORDER BY 2 DESC LIMIT 12", "bar");
            }

            Charts = await _svc.ListProjectChartsAsync(ProjectId);
        }
        catch { }
    }

    public void ToggleChartEditor()
    {
        ShowChartEditor = !ShowChartEditor;
        ChartMonacoReady = false;
        ChartError = null;
        if (ShowChartEditor && string.IsNullOrWhiteSpace(ChartSql) && Entity is not null)
            ChartSql = $"SELECT \"{LabelCol()}\", count(*) FROM \"{Entity.TableName}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 10";
        NotifyStateChanged();
    }

    public async Task SaveEntityChartAsync(Func<string, string, Task<string>>? getMonacoValue = null)
    {
        if (Entity is null) return;
        if (ChartMonaco && ChartMonacoReady && getMonacoValue is not null)
        {
            try { ChartSql = await getMonacoValue(ChartEditorId, ChartSql); } catch { }
        }
        ChartError = null;
        if (string.IsNullOrWhiteSpace(ChartName)) { ChartError = "Give the chart a name."; NotifyStateChanged(); return; }
        ChartBusy = true;
        try
        {
            await _svc.SaveSqlChartAsync(ProjectId, $"{Entity.Name} · {ChartName.Trim()}", ChartSql, ChartType);
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            ShowChartEditor = false;
            ChartName = "";
        }
        catch (Exception ex) { ChartError = ex.Message; }
        finally { ChartBusy = false; NotifyStateChanged(); }
    }

    public async Task SwitchChartTypeAsync(ProjectChartView chart, string type)
    {
        if (StoredSql(chart) is not { } sql || StoredType(chart) == type) return;
        try
        {
            await _svc.SaveSqlChartAsync(ProjectId, chart.TableName["sql:".Length..], sql, type);
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex) { ChartError = ex.Message; NotifyStateChanged(); }
    }

    public async Task RefreshChartAsync(ProjectChartView chart)
    {
        if (StoredSql(chart) is not { } sql) return;
        try
        {
            await _svc.SaveSqlChartAsync(ProjectId, chart.TableName["sql:".Length..], sql, StoredType(chart));
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex) { ChartError = ex.Message; NotifyStateChanged(); }
    }

    public async Task DeleteChartAsync(ProjectChartView chart)
    {
        try
        {
            await _svc.DeleteSqlChartAsync(ProjectId, chart.TableName["sql:".Length..]);
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex) { ChartError = ex.Message; NotifyStateChanged(); }
    }

    // ── Graph ─────────────────────────────────────────────────────────────────────────────────
    public async Task BuildSectionGraphAsync()
    {
        BrainGraph = null;
        if (Entity is null || Rows is null || Rows.Rows.Count == 0) return;
        try
        {
            const int maxRecords = 12;
            var cols = Rows.Columns.ToList();
            var sourceRows = FocusKey is { } focus
                ? Rows.Rows.Where(r => r.Any(v => string.Equals(v, focus, StringComparison.OrdinalIgnoreCase))).ToList()
                : Rows.Rows;
            var labelIdx = LabelCol() is { } lc ? Math.Max(0, cols.FindIndex(c => string.Equals(c, lc, StringComparison.OrdinalIgnoreCase))) : 0;
            var records = sourceRows.Take(maxRecords)
                .Select(r => labelIdx < r.Count ? r[labelIdx] ?? "—" : "—").ToList();

            var keyToRecord = new Dictionary<string, int>(StringComparer.Ordinal);
            var keyColsList = new List<int> { labelIdx };
            foreach (var rel in Entity.Relations)
            {
                var ci = cols.FindIndex(c => string.Equals(c, rel.Column, StringComparison.OrdinalIgnoreCase));
                if (ci >= 0) keyColsList.Add(ci);
            }
            for (var i = 0; i < Math.Min(maxRecords, sourceRows.Count); i++)
            {
                foreach (var ci in keyColsList.Distinct())
                    if (ci < sourceRows[i].Count && sourceRows[i][ci] is { Length: > 2 } v)
                        keyToRecord.TryAdd(v, i);
            }

            var relatedNodes = new List<(string Entity, string Label)>();
            var relEdges = new List<(int Record, int Related)>();
            foreach (var rel in Entity.Relations.Take(2))
            {
                var target = AllEntities.FirstOrDefault(e => string.Equals(e.Name, rel.TargetEntity, StringComparison.OrdinalIgnoreCase));
                if (target is null) continue;
                var tLabel = target.LabelColumn ?? rel.TargetColumn;
                try
                {
                    var join = await _svc.ExecuteProjectDataAsync(ProjectId,
                        $"SELECT a.\"{rel.Column}\"::text, b.\"{tLabel.Replace("\"", "")}\"::text FROM \"{Entity.TableName.Replace("\"", "")}\" a JOIN \"{target.TableName.Replace("\"", "")}\" b ON a.\"{rel.Column}\"::text = b.\"{rel.TargetColumn.Replace("\"", "")}\"::text LIMIT 40");
                    foreach (var row in join.Rows)
                    {
                        if (row.Count < 2 || row[0] is not { } k || row[1] is not { } lbl) continue;
                        if (!keyToRecord.TryGetValue(k, out var recIdx)) continue;
                        var ni = relatedNodes.FindIndex(n => n.Entity == target.Name && n.Label == lbl);
                        if (ni < 0) { relatedNodes.Add((target.Name, lbl)); ni = relatedNodes.Count - 1; }
                        if (relatedNodes.Count > maxRecords) break;
                        if (!relEdges.Contains((recIdx, ni))) relEdges.Add((recIdx, ni));
                    }
                }
                catch { }
            }

            var pairs = await _svc.ListEntityTagPairsAsync(Entity.Id);
            var runNodes = new List<Guid>();
            var runEdges = new List<(int Record, int Run)>();
            foreach (var pair in pairs)
            {
                if (!keyToRecord.TryGetValue(pair.Key, out var recIdx)) continue;
                var ri = runNodes.IndexOf(pair.RunId);
                if (ri < 0)
                {
                    if (runNodes.Count >= 8) continue;
                    runNodes.Add(pair.RunId);
                    ri = runNodes.Count - 1;
                }
                if (!runEdges.Contains((recIdx, ri))) runEdges.Add((recIdx, ri));
            }

            var allArts = new List<(int Run, RunArtifactLinkView Art)>();
            for (var ri = 0; ri < runNodes.Count; ri++)
            {
                try { foreach (var a in await _svc.ListRunArtifactsAsync(runNodes[ri])) allArts.Add((ri, a)); }
                catch { }
            }
            var artNodes = allArts
                .GroupBy(x => (x.Art.Kind, x.Art.Title))
                .Select(g => (g.OrderByDescending(x => x.Art.CreatedAt).First(), Versions: g.Count()))
                .OrderByDescending(x => x.Item1.Art.CreatedAt)
                .Take(14)
                .Select(x => (x.Item1.Run, x.Item1.Art, x.Versions))
                .ToList();

            BrainGraph = BuildBrain(records, relatedNodes, relEdges, runNodes, runEdges, artNodes);
        }
        catch { }
        NotifyStateChanged();
    }

    private GraphVizView BuildBrain(
        List<string> records,
        List<(string Entity, string Label)> related,
        List<(int Record, int Related)> relEdges,
        List<Guid> runs,
        List<(int Record, int Run)> runEdges,
        List<(int Run, RunArtifactLinkView Art, int Versions)> arts)
    {
        var nodes = new List<GraphNodeView>();
        var links = new List<GraphLinkView>();
        var degree = new Dictionary<string, int>();

        void Link(string a, string b)
        {
            links.Add(new GraphLinkView(a, b, "Direct"));
            degree[a] = degree.GetValueOrDefault(a) + 1;
            degree[b] = degree.GetValueOrDefault(b) + 1;
        }

        for (var i = 0; i < records.Count; i++)
            nodes.Add(new GraphNodeView($"rec:{i}", records[i] ?? "—", 0, IsGod: true,
                Content: $"{Entity?.Name} record"));
        NodeUrls.Clear();
        for (var i = 0; i < related.Count; i++)
        {
            nodes.Add(new GraphNodeView($"rel:{i}", related[i].Label, 0, false,
                Content: $"{related[i].Entity} record (related)", Kind: "human"));
            NodeUrls[$"rel:{i}"] = $"/project/{ProjectId}/entity/{Uri.EscapeDataString(related[i].Entity)}?record={Uri.EscapeDataString(related[i].Label)}";
        }
        for (var i = 0; i < runs.Count; i++)
        {
            nodes.Add(new GraphNodeView($"run:{i}", $"run {runs[i].ToString("N")[..8]}", 0, false,
                Content: "tagged job run — its output mentioned the connected records"));
            NodeUrls[$"run:{i}"] = $"/observability?run={runs[i]}";
        }
        for (var i = 0; i < arts.Count; i++)
        {
            nodes.Add(new GraphNodeView($"art:{i}",
                arts[i].Versions > 1 ? $"{arts[i].Art.Title} · v{arts[i].Versions}" : arts[i].Art.Title, 0, false,
                Content: arts[i].Versions > 1
                    ? $"artifact · {arts[i].Art.Kind} · latest of {arts[i].Versions} versions"
                    : $"artifact · {arts[i].Art.Kind}", Kind: "good", Labeled: true));
            NodeUrls[$"art:{i}"] = $"/artifacts?artifact={arts[i].Art.Id}";
        }

        foreach (var (rec, rel) in relEdges) Link($"rec:{rec}", $"rel:{rel}");
        foreach (var (rec, run) in runEdges) Link($"rec:{rec}", $"run:{run}");
        for (var i = 0; i < arts.Count; i++) Link($"run:{arts[i].Run}", $"art:{i}");

        nodes = nodes.Select(n => n with { Degree = degree.GetValueOrDefault(n.Id) }).ToList();
        return new GraphVizView(ProjectId, nodes.Count, links.Count, nodes, links);
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
