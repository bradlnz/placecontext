using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataMapViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly IJSRuntime _js;

    public DataMapViewModel(IPlaceContextService svc, IJSRuntime js)
    {
        _svc = svc;
        _js = js;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Data state ────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<DataMappingView>? Mappings { get; private set; }
    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public IReadOnlyList<ProjectTableInfo>? Tables { get; private set; }
    public IReadOnlyList<JobChainView>? Chains { get; private set; }
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;

    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public bool Suggesting { get; private set; }
    public Guid? EditId { get; private set; }
    public Guid EdJobId { get; set; }
    public string EdSourceKind { get; set; } = "job";
    public string EdTable { get; set; } = "";
    public string EdRowsPath { get; set; } = "";
    public bool EdEnabled { get; set; } = true;
    public string? EditorError { get; private set; }
    public List<FieldEdit> EdFields { get; } = new();

    // ── Canvas state ──────────────────────────────────────────────────────────────────────────
    public Dictionary<string, (double X, double Y)> Pos { get; } = new();
    public bool PosLoaded { get; private set; }
    public string? DragKey { get; set; }
    public double PanX { get; set; }
    public double PanY { get; set; }
    public bool Panning { get; set; }
    public string? ConnectJobId { get; set; }
    public JobView? ConnectJob { get; set; }
    public JobChainView? ConnectChain { get; set; }
    public (double X, double Y) ConnectEnd { get; set; }
    public double LastX { get; set; }
    public double LastY { get; set; }
    public bool Moved { get; set; }

    public static readonly string[] ColumnTypes = { "text", "integer", "bigint", "numeric", "boolean", "timestamptz", "date", "uuid", "jsonb" };

    // ── Data helpers ──────────────────────────────────────────────────────────────────────────
    public string ReturnTypeOf(Guid jobId)
        => Jobs?.FirstOrDefault(j => j.Id == jobId)?.ReturnType.ToString() ?? "?";

    public long? TableRows(string table)
        => Tables?.FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))?.RowEstimate;

    public IReadOnlyList<JobView> UnmappedJobs()
        => (Jobs ?? Array.Empty<JobView>())
            .Where(j => Mappings?.All(m => m.JobId != j.Id) ?? true)
            .ToList();

    public IReadOnlyList<string> TableNodes()
    {
        var mapped = (Mappings ?? Array.Empty<DataMappingView>()).Select(m => m.TargetTable);
        var real = (Tables ?? Array.Empty<ProjectTableInfo>())
            .Where(t => !t.IsView).Select(t => t.Name);
        return mapped.Concat(real).Distinct(StringComparer.OrdinalIgnoreCase).Take(14).ToList();
    }

    public (double X, double Y) GetPos(string key) => Pos.TryGetValue(key, out var p) ? p : (24, 24);

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId) => ProjectId = projectId;

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            Mappings = await _svc.ListDataMappingsAsync(ProjectId);
            Jobs = await _svc.ListJobsAsync(ProjectId);
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            try { Tables = await _svc.ListProjectDataTablesAsync(ProjectId); }
            catch { Tables = Array.Empty<ProjectTableInfo>(); }
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

    public async Task EnsureLayoutAsync()
    {
        if (PosLoaded) return;
        PosLoaded = true;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", $"pc-datamap-{ProjectId}");
            if (!string.IsNullOrEmpty(json))
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(json);
                if (saved is not null)
                    foreach (var (k, v) in saved.Where(kv => kv.Value.Length == 2))
                        Pos[k] = (v[0], v[1]);
            }
        }
        catch { }
        DefaultLayout();
    }

    private void DefaultLayout()
    {
        var yJ = 24d;
        foreach (var j in Jobs ?? Array.Empty<JobView>())
        {
            Pos.TryAdd("job:" + j.Id, (30, yJ));
            yJ += 84;
        }
        foreach (var c in Chains ?? Array.Empty<JobChainView>())
        {
            Pos.TryAdd("chain:" + c.Id, (30, yJ));
            yJ += 84;
        }
        var yT = 24d;
        foreach (var t in TableNodes())
        {
            Pos.TryAdd("table:" + t, (660, yT));
            yT += 96;
        }
    }

    public async Task SavePositionsAsync()
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(
                Pos.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.X, kv.Value.Y }));
            await _js.InvokeVoidAsync("localStorage.setItem", $"pc-datamap-{ProjectId}", payload);
        }
        catch { }
    }

    // ── Canvas drag ───────────────────────────────────────────────────────────────────────────
    public void StartDrag(string key, double clientX, double clientY)
    {
        DragKey = key;
        LastX = clientX;
        LastY = clientY;
        Moved = false;
    }

    public void OnCanvasDown(double clientX, double clientY)
    {
        Panning = true;
        LastX = clientX;
        LastY = clientY;
    }

    public void StartConnect(JobView job, double clientX, double clientY)
    {
        ConnectJob = job;
        ConnectJobId = "job:" + job.Id;
        var p = GetPos(ConnectJobId);
        ConnectEnd = (p.X + 190, p.Y + 22);
        LastX = clientX;
        LastY = clientY;
    }

    public void StartConnectChain(JobChainView chain, double clientX, double clientY)
    {
        ConnectChain = chain;
        ConnectJobId = "chain:" + chain.Id;
        var cp = GetPos(ConnectJobId);
        ConnectEnd = (cp.X + 190, cp.Y + 22);
        LastX = clientX;
        LastY = clientY;
    }

    public void OnCanvasMove(double clientX, double clientY)
    {
        var dx = clientX - LastX;
        var dy = clientY - LastY;
        if (DragKey is { } key)
        {
            var p = GetPos(key);
            Pos[key] = (Math.Max(0, p.X + dx), Math.Max(0, p.Y + dy));
            if (Math.Abs(dx) + Math.Abs(dy) > 1) Moved = true;
        }
        else if (ConnectJob is not null || ConnectChain is not null)
        {
            ConnectEnd = (ConnectEnd.X + dx, ConnectEnd.Y + dy);
        }
        else if (Panning)
        {
            PanX += dx;
            PanY += dy;
        }
        else return;
        LastX = clientX;
        LastY = clientY;
    }

    public async Task OnCanvasUpAsync()
    {
        if (Panning) { Panning = false; return; }
        if (DragKey is not null)
        {
            var clickedJob = !Moved && DragKey.StartsWith("job:") ? DragKey["job:".Length..] : null;
            DragKey = null;
            await SavePositionsAsync();
            if (clickedJob is not null && Guid.TryParse(clickedJob, out var jobId))
            {
                if (Mappings?.FirstOrDefault(m => m.JobId == jobId) is { } existing) OpenEditor(existing);
                else if (Jobs?.FirstOrDefault(j => j.Id == jobId) is { } job) OpenEditorForJob(job);
            }
        }
        else if (ConnectJob is { } job)
        {
            ConnectJob = null;
            OpenEditorForJob(job);
        }
        else if (ConnectChain is { } chain)
        {
            ConnectChain = null;
            OpenEditorForChain(chain);
        }
    }

    public async Task OnTableUpAsync(string table)
    {
        if (ConnectJob is { } job)
        {
            ConnectJob = null;
            OpenEditorForJob(job);
            EdTable = table;
            return;
        }
        if (ConnectChain is { } chain)
        {
            ConnectChain = null;
            OpenEditorForChain(chain);
            EdTable = table;
            return;
        }
        if (DragKey is not null)
        {
            DragKey = null;
            await SavePositionsAsync();
        }
    }

    // ── Edge paths ────────────────────────────────────────────────────────────────────────────
    public string EdgePath(DataMappingView m)
    {
        var from = GetPos((m.SourceKind == "chain" ? "chain:" : "job:") + m.JobId);
        var to = GetPos("table:" + m.TargetTable);
        var x1 = from.X + 190 + PanX; var y1 = from.Y + 26 + PanY;
        var x2 = to.X + PanX; var y2 = to.Y + 26 + PanY;
        var bend = Math.Max(40, (x2 - x1) / 2);
        return FormattableString.Invariant($"M {x1} {y1} C {x1 + bend} {y1}, {x2 - bend} {y2}, {x2} {y2}");
    }

    public string ConnectPath()
    {
        if (ConnectJobId is null) return "";
        var from = GetPos(ConnectJobId);
        var x1 = from.X + 190 + PanX; var y1 = from.Y + 22 + PanY;
        return FormattableString.Invariant($"M {x1} {y1} L {ConnectEnd.X + PanX} {ConnectEnd.Y + PanY}");
    }

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void OpenEditor(DataMappingView? m)
    {
        EditId = m?.Id;
        EdJobId = m?.JobId ?? Guid.Empty;
        EdSourceKind = m?.SourceKind ?? "job";
        EdTable = m?.TargetTable ?? "";
        EdRowsPath = m?.RowsPath ?? "";
        EdEnabled = m?.Enabled ?? true;
        EdFields.Clear();
        if (m is not null)
            EdFields.AddRange(m.Fields.Select(f => new FieldEdit { SourcePath = f.SourcePath, Column = f.Column, Type = f.Type }));
        EditorError = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void OpenEditorForJob(JobView job)
    {
        OpenEditor(null);
        EdSourceKind = "job";
        EdJobId = job.Id;
        EdTable = Sanitize(job.Name);
    }

    public void OpenEditorForChain(JobChainView chain)
    {
        OpenEditor(null);
        EdSourceKind = "chain";
        EdJobId = chain.Id;
        EdTable = Sanitize(chain.Name);
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        EditorError = null;
        NotifyStateChanged();
    }

    public void AddField() { EdFields.Add(new FieldEdit()); NotifyStateChanged(); }

    public void RemoveField(int idx)
    {
        if (idx >= 0 && idx < EdFields.Count) { EdFields.RemoveAt(idx); NotifyStateChanged(); }
    }

    public async Task SaveMappingAsync()
    {
        EditorError = null;
        if (EdJobId == Guid.Empty) { EditorError = EdSourceKind == "chain" ? "Pick a source chain." : "Pick a source job."; NotifyStateChanged(); return; }
        if (string.IsNullOrWhiteSpace(EdTable)) { EditorError = "Target table is required."; NotifyStateChanged(); return; }
        var fields = EdFields
            .Where(f => !string.IsNullOrWhiteSpace(f.SourcePath) || !string.IsNullOrWhiteSpace(f.Column))
            .Select(f => new DataFieldDto(f.SourcePath.Trim(), f.Column.Trim(), f.Type))
            .ToList();
        if (fields.Count == 0) { EditorError = "Add at least one field."; NotifyStateChanged(); return; }

        Saving = true;
        try
        {
            await _svc.SaveDataMappingAsync(new SaveDataMappingCommand(
                ProjectId, EdJobId, EdTable.Trim(),
                string.IsNullOrWhiteSpace(EdRowsPath) ? null : EdRowsPath.Trim(),
                fields, EdEnabled, EditId,
                SourceKind: EdSourceKind));
            await LoadAsync();
            ShowEditor = false;
        }
        catch (Exception ex) { EditorError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }

    public async Task DeleteMappingAsync()
    {
        if (EditId is not { } id) return;
        Saving = true;
        try
        {
            await _svc.DeleteDataMappingAsync(id);
            await LoadAsync();
            ShowEditor = false;
        }
        catch (Exception ex) { EditorError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }

    public async Task SuggestFieldsAsync()
    {
        if (EdJobId == Guid.Empty) return;
        Suggesting = true;
        EditorError = null;
        try
        {
            var runs = await _svc.ListJobRunsAsync(EdJobId);
            var last = runs.FirstOrDefault(r => r.Status is "Succeeded" or "Partial") ?? runs.FirstOrDefault();
            if (last is null) { EditorError = "This job has no runs yet — run it once, then suggest."; return; }
            var detail = await _svc.GetJobRunAsync(last.Id);
            var artifact = detail?.ReduceResult?.Artifact
                ?? detail?.ShardResults.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Artifact))?.Artifact;
            if (string.IsNullOrWhiteSpace(artifact)) { EditorError = "The latest run has no result to sample."; return; }

            using var doc = System.Text.Json.JsonDocument.Parse(artifact);
            var el = doc.RootElement;

            if (!string.IsNullOrWhiteSpace(EdRowsPath))
            {
                foreach (var seg in EdRowsPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (el.ValueKind != System.Text.Json.JsonValueKind.Object || !el.TryGetProperty(seg, out el))
                    { EditorError = $"Path '{EdRowsPath}' not found in the latest result."; return; }
                }
            }
            else if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var firstArray = el.EnumerateObject().FirstOrDefault(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Array);
                if (firstArray.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    EdRowsPath = firstArray.Name;
                    el = firstArray.Value;
                }
            }

            var sample = el.ValueKind == System.Text.Json.JsonValueKind.Array
                ? el.EnumerateArray().FirstOrDefault()
                : el;
            if (sample.ValueKind != System.Text.Json.JsonValueKind.Object)
            { EditorError = "Couldn't find a record object to sample in the latest result."; return; }

            EdFields.Clear();
            foreach (var p in sample.EnumerateObject())
                EdFields.Add(new FieldEdit { SourcePath = p.Name, Column = Sanitize(p.Name), Type = InferType(p.Value) });
        }
        catch (Exception ex) { EditorError = $"Couldn't sample the latest run: {ex.Message}"; }
        finally { Suggesting = false; NotifyStateChanged(); }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────────────────────
    public static string InferType(System.Text.Json.JsonElement v) => v.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Number => v.TryGetInt64(out _) ? "bigint" : "numeric",
        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
        System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array => "jsonb",
        System.Text.Json.JsonValueKind.String when DateTimeOffset.TryParse(v.GetString(), out _) => "timestamptz",
        _ => "text",
    };

    public static string Sanitize(string name)
    {
        var s = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (s.Length == 0) s = "col";
        if (char.IsDigit(s[0])) s = "_" + s;
        return s.Length > 63 ? s[..63] : s;
    }
}

public sealed class FieldEdit
{
    public string SourcePath { get; set; } = "";
    public string Column { get; set; } = "";
    public string Type { get; set; } = "text";
}
