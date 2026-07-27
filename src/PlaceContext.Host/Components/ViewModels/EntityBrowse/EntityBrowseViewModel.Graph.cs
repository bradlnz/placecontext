using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class EntityBrowseViewModel
{
    // ── Graph ─────────────────────────────────────────────────────────────────────────────────
    public GraphVizView? BrainGraph { get; private set; }
    public Dictionary<string, string> NodeUrls { get; } = new();
    public string? FocusKey { get; set; }
    public bool ShowGraph { get; set; }

    // ── Insights ──────────────────────────────────────────────────────────────────────────────
    public sealed record Insight(string Title, string? Big, string? Sub,
        IReadOnlyList<(string Label, string Count, int Frac)> Bars);
    public List<Insight> Insights { get; } = new();

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

}
