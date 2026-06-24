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

    public DecisionTree Assemble(
        ProjectName projectName,
        IReadOnlyList<Decision> decisions,
        ChangeLedger ledger,
        IReadOnlyList<ToolActivity> activity)
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

        // Tool-call activity is summarized as one branch, one node per tool (degree = call count).
        if (activity.Count > 0)
        {
            const string activityId = "activity";
            AddNode(activityId, "MCP activity", TreeNodeKind.Activity);
            edges.Add(new DecisionTreeEdge(rootId, activityId, ConfidenceTag.Extracted));

            foreach (var g in activity.GroupBy(a => a.Tool).OrderBy(g => g.Key))
            {
                var count = g.Count();
                var failed = g.Any(a => a.Failed);
                var toolId = "tool:" + g.Key;
                AddNode(toolId, $"{g.Key} ×{count}", TreeNodeKind.Tool);
                edges.Add(new DecisionTreeEdge(activityId, toolId, failed ? ConfidenceTag.Ambiguous : ConfidenceTag.Extracted));
            }
        }

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

    private static string Clip(string s) => s.Length <= MaxLabel ? s : s[..(MaxLabel - 1)] + "…";
}
