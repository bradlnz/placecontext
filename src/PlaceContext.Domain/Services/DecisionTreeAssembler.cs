using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service that assembles a <see cref="DecisionTree"/> from everything PlaceContext has
/// logged for a project — decisions, the change ledger, and MCP tool activity. This is the
/// replacement for graphify: the project's structure is derived from recorded activity, not from
/// parsing source. Deterministic and I/O-free, so the Application just feeds it loaded aggregates.
/// </summary>
public sealed class DecisionTreeAssembler
{
    /// <summary>A file/node touched by at least this many changes is a churn hotspot ("god node").</summary>
    private const int HotspotThreshold = 3;
    private const int MaxHotspots = 25;
    private const int MaxLabel = 80;

    /// <summary>Two run-output nodes are linked when their embeddings are at least this cosine-similar.</summary>
    private const double SimilarityThreshold = 0.6;
    /// <summary>Each run-output node links to at most this many of its nearest semantic neighbours.</summary>
    private const int MaxSimilarLinks = 3;

    public DecisionTree Assemble(
        ProjectName projectName,
        IReadOnlyList<Decision> decisions,
        ActivityLog ledger,
        IReadOnlyList<ToolActivity> activity,
        IReadOnlyList<RunOutputNode>? runOutputs = null)
    {
        const string rootId = "root";
        var specs = new List<(string Id, string Label, TreeNodeKind Kind)> { (rootId, projectName.Value, TreeNodeKind.Root) };
        var edges = new List<DecisionTreeEdge>();
        var seen = new HashSet<string> { rootId };

        void AddNode(string id, string label, TreeNodeKind kind)
        {
            if (seen.Add(id)) specs.Add((id, label, kind));
        }

        // Decisions branch off the root, oldest first, so a change can attach to the latest prior decision.
        var ordered = decisions.OrderBy(d => d.DecidedAt).ToList();
        var decisionIds = new Dictionary<DecisionId, string>();
        foreach (var d in ordered)
        {
            var id = "decision:" + d.Id.Value.ToString("N")[..8];
            decisionIds[d.Id] = id;
            AddNode(id, Clip($"{d.Question} → {d.Choice}"), TreeNodeKind.Decision);
            edges.Add(new DecisionTreeEdge(rootId, id, ConfidenceTag.Extracted));
        }

        // Changes hang beneath the decision that preceded them (or the root if none), and their touched
        // files/nodes hang beneath each change — shared files accrue degree, surfacing churn hotspots.
        foreach (var c in ledger.Records.OrderBy(r => r.Sequence))
        {
            var changeId = "change:" + c.Sequence;
            AddNode(changeId, Clip(string.IsNullOrWhiteSpace(c.Summary) ? $"change {c.Sequence}" : c.Summary), TreeNodeKind.Change);

            var parent = ordered.LastOrDefault(d => d.DecidedAt <= c.RecordedAt);
            var (parentId, conf) = parent is not null
                ? (decisionIds[parent.Id], ConfidenceTag.Extracted)
                : (rootId, ConfidenceTag.Inferred); // orphan change = lower confidence
            edges.Add(new DecisionTreeEdge(parentId, changeId, conf));

            var touched = c.TouchedFiles.Concat(c.TouchedNodes.Select(n => n.Value))
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct();
            foreach (var t in touched)
            {
                var fileId = "file:" + t;
                AddNode(fileId, t, TreeNodeKind.File);
                edges.Add(new DecisionTreeEdge(changeId, fileId, ConfidenceTag.Extracted));
            }
        }

        // MCP tool-call activity is intentionally NOT shown in the dependency graph — it is transient
        // access, not a structural dependency, and clutters the picture. (It remains in the activity log
        // and the MCP view.) The parameter is retained for API stability.
        _ = activity;

        // Job-run outputs become the project's "brain": each embedded run output is a node off the root,
        // then cross-linked to its most semantically-similar peers (cosine over the embedding vectors) so
        // the accumulated outputs weave the dependency graph together into a queryable memory. The
        // cross-links are Inferred — they are semantic, not extracted from a recorded relationship.
        WeaveRunOutputs(runOutputs, rootId, edges, seen, AddNode);

        // Degree = incident-edge count. For file nodes that equals the number of changes touching it.
        var degree = new Dictionary<string, int>();
        foreach (var e in edges)
        {
            degree[e.ParentId] = degree.GetValueOrDefault(e.ParentId) + 1;
            degree[e.ChildId] = degree.GetValueOrDefault(e.ChildId) + 1;
        }

        var hotspotIds = specs
            .Where(s => s.Kind == TreeNodeKind.File && degree.GetValueOrDefault(s.Id) >= HotspotThreshold)
            .OrderByDescending(s => degree.GetValueOrDefault(s.Id))
            .Take(MaxHotspots)
            .Select(s => s.Id)
            .ToHashSet();

        var nodes = specs.Select(s => new DecisionTreeNode(
            s.Id, s.Label, s.Kind, degree.GetValueOrDefault(s.Id), hotspotIds.Contains(s.Id))).ToList();

        return DecisionTree.Of(nodes, edges);
    }

    /// <summary>
    /// Adds one <see cref="TreeNodeKind.JobRunOutput"/> node per embedded run output (hung off the root),
    /// then links each to its top-<see cref="MaxSimilarLinks"/> nearest peers above
    /// <see cref="SimilarityThreshold"/> cosine similarity. Undirected — symmetric pairs are de-duplicated.
    /// </summary>
    private static void WeaveRunOutputs(
        IReadOnlyList<RunOutputNode>? runOutputs,
        string rootId,
        List<DecisionTreeEdge> edges,
        HashSet<string> seen,
        Action<string, string, TreeNodeKind> addNode)
    {
        if (runOutputs is not { Count: > 0 }) return;

        var outputs = new List<(string Id, float[] Vector)>();
        foreach (var o in runOutputs)
        {
            if (o.Vector.Count == 0) continue;
            var id = "runoutput:" + o.Id;
            if (!seen.Contains(id))
            {
                addNode(id, Clip(CleanLabel(o.Label, id)), TreeNodeKind.JobRunOutput);
                edges.Add(new DecisionTreeEdge(rootId, id, ConfidenceTag.Extracted));
                outputs.Add((id, o.Vector.ToArray()));
            }
        }

        var linked = new HashSet<string>();
        for (var i = 0; i < outputs.Count; i++)
        {
            var neighbours = new List<(int J, double Sim)>();
            for (var j = 0; j < outputs.Count; j++)
            {
                if (i == j) continue;
                var sim = Cosine(outputs[i].Vector, outputs[j].Vector);
                if (sim >= SimilarityThreshold) neighbours.Add((j, sim));
            }

            foreach (var (j, _) in neighbours.OrderByDescending(n => n.Sim).Take(MaxSimilarLinks))
            {
                var a = outputs[i].Id;
                var b = outputs[j].Id;
                var key = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
                if (linked.Add(key))
                    edges.Add(new DecisionTreeEdge(a, b, ConfidenceTag.Inferred));
            }
        }
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Strips leading Markdown header markers/whitespace from an embedded output's first line.</summary>
    private static string CleanLabel(string label, string fallback)
    {
        var line = (label ?? string.Empty).Split('\n', 2)[0].TrimStart('#', ' ', '\t').Trim();
        return string.IsNullOrWhiteSpace(line) ? fallback : line;
    }

    private static string Clip(string s) => s.Length <= MaxLabel ? s : s[..(MaxLabel - 1)] + "…";
}
