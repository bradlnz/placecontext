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
    /// <summary>Node content (rationale / run-output text) is clipped to this for the detail panel.</summary>
    private const int MaxContent = 500;

    /// <summary>Two run-output nodes are linked when their embeddings are at least this cosine-similar.</summary>
    private const double SimilarityThreshold = 0.6;
    /// <summary>Each run-output node links to at most this many of its nearest semantic neighbours.</summary>
    private const int MaxSimilarLinks = 3;

    public DecisionTree Assemble(
        ProjectName projectName,
        IReadOnlyList<Decision> decisions,
        ActivityLog ledger,
        IReadOnlyList<ToolActivity> activity,
        IReadOnlyList<RunOutputNode>? runOutputs = null,
        IReadOnlyList<Job>? jobs = null,
        IReadOnlyList<JobChain>? chains = null,
        IReadOnlyList<DataMapping>? mappings = null,
        IReadOnlyList<string>? tables = null,
        IReadOnlyList<DataEntity>? entities = null)
    {
        const string rootId = "root";
        var specs = new List<(string Id, string Label, TreeNodeKind Kind, string? Content)>
        {
            (rootId, projectName.Value, TreeNodeKind.Root, null),
        };
        var edges = new List<DecisionTreeEdge>();
        var seen = new HashSet<string> { rootId };

        void AddNode(string id, string label, TreeNodeKind kind, string? content = null)
        {
            if (seen.Add(id)) specs.Add((id, label, kind, ClipContent(content)));
        }

        // Decisions branch off the root, oldest first, so a change can attach to the latest prior decision.
        var ordered = decisions.OrderBy(d => d.DecidedAt).ToList();
        var decisionIds = new Dictionary<DecisionId, string>();
        foreach (var d in ordered)
        {
            var id = "decision:" + d.Id.Value.ToString("N")[..8];
            decisionIds[d.Id] = id;
            AddNode(id, Clip($"{d.Question} → {d.Choice}"), TreeNodeKind.Decision, d.Rationale.Value);
            edges.Add(new DecisionTreeEdge(rootId, id, ConfidenceTag.Extracted));
        }

        // Changes hang beneath the decision that preceded them (or the root if none), and their touched
        // files/nodes hang beneath each change — shared files accrue degree, surfacing churn hotspots.
        foreach (var c in ledger.Records.OrderBy(r => r.Sequence))
        {
            var changeId = "change:" + c.Sequence;
            AddNode(changeId, Clip(string.IsNullOrWhiteSpace(c.Summary) ? $"change {c.Sequence}" : c.Summary),
                TreeNodeKind.Change, c.Rationale.Value);

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

        // ── Project lineage: chains → their jobs, jobs → the tables they write (via data
        // mappings). This is the structural dependency graph of the data platform — derived
        // from recorded configuration, no embeddings needed. ──
        var jobNodeIds = new Dictionary<Guid, string>();
        foreach (var job in jobs ?? (IReadOnlyList<Job>)Array.Empty<Job>())
        {
            var id = "job:" + job.Id.ToString("N");
            jobNodeIds[job.Id] = id;
            AddNode(id, Clip(job.Name), TreeNodeKind.Job, job.Description);
        }

        var chainedJobIds = new HashSet<Guid>();
        foreach (var chain in chains ?? (IReadOnlyList<JobChain>)Array.Empty<JobChain>())
        {
            var chainId = "chain:" + chain.Id.ToString("N");
            AddNode(chainId, Clip(chain.Name), TreeNodeKind.Chain, chain.Description);
            edges.Add(new DecisionTreeEdge(rootId, chainId, ConfidenceTag.Extracted));

            // Chain membership edges plus stage-to-stage dependency edges (stage N feeds stage N+1).
            string? previousStageJobId = null;
            foreach (var stage in chain.Stages)
            {
                foreach (var jobId in stage.JobIds)
                {
                    if (!jobNodeIds.TryGetValue(jobId, out var jobNodeId)) continue;
                    chainedJobIds.Add(jobId);
                    edges.Add(new DecisionTreeEdge(chainId, jobNodeId, ConfidenceTag.Extracted));
                    if (previousStageJobId is not null)
                        edges.Add(new DecisionTreeEdge(previousStageJobId, jobNodeId, ConfidenceTag.Extracted));
                }
                // For parallel fan-out stages the dependency fans in from the single previous stage.
                if (stage.JobIds.Count > 0 && jobNodeIds.TryGetValue(stage.JobIds[0], out var firstOfStage))
                    previousStageJobId = firstOfStage;
            }
        }

        // Jobs not in any chain hang directly off the root.
        foreach (var (jobId, nodeId) in jobNodeIds)
        {
            if (!chainedJobIds.Contains(jobId))
                edges.Add(new DecisionTreeEdge(rootId, nodeId, ConfidenceTag.Extracted));
        }

        // Tables written by jobs (data mappings) and any remaining project tables off the root.
        var mappedTables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings ?? (IReadOnlyList<DataMapping>)Array.Empty<DataMapping>())
        {
            if (!jobNodeIds.TryGetValue(mapping.JobId, out var jobNodeId)) continue;
            var tableId = "table:" + mapping.TargetTable;
            mappedTables.Add(mapping.TargetTable);
            AddNode(tableId, Clip(mapping.TargetTable), TreeNodeKind.Table);
            edges.Add(new DecisionTreeEdge(jobNodeId, tableId, ConfidenceTag.Extracted));
        }
        foreach (var table in tables ?? (IReadOnlyList<string>)Array.Empty<string>())
        {
            if (mappedTables.Contains(table)) continue;
            var tableId = "table:" + table;
            AddNode(tableId, Clip(table), TreeNodeKind.Table);
            edges.Add(new DecisionTreeEdge(rootId, tableId, ConfidenceTag.Extracted));
        }

        // Business entities: a tagged view over a project table, with declared relations
        // between entities (this column matches that entity's column).
        var entityNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityList = entities ?? (IReadOnlyList<DataEntity>)Array.Empty<DataEntity>();
        foreach (var entity in entityList)
        {
            var id = "entity:" + entity.Id.ToString("N");
            entityNodeIds[entity.Name] = id;
            var content = entity.Tags.Count > 0 ? "tags: " + string.Join(", ", entity.Tags) : null;
            AddNode(id, Clip(entity.Name), TreeNodeKind.Entity, content);

            // The entity reads from its table when that table is in the graph, else off the root.
            var tableId = "table:" + entity.TableName;
            var parentId = seen.Contains(tableId) ? tableId : rootId;
            edges.Add(new DecisionTreeEdge(parentId, id, ConfidenceTag.Extracted));
        }
        foreach (var entity in entityList)
        {
            foreach (var relation in entity.Relations)
            {
                if (entityNodeIds.TryGetValue(entity.Name, out var from)
                    && entityNodeIds.TryGetValue(relation.TargetEntity, out var to))
                {
                    edges.Add(new DecisionTreeEdge(from, to, ConfidenceTag.Extracted));
                }
            }
        }

        // Job-run outputs become the project's "brain": each embedded run output is a node off its
        // job (or the root when the job is unknown), then cross-linked to its most semantically-similar
        // peers (cosine over the embedding vectors) so the accumulated outputs weave the dependency
        // graph together into a queryable memory. The cross-links are Inferred — they are semantic,
        // not extracted from a recorded relationship.
        WeaveRunOutputs(runOutputs, rootId, edges, seen, AddNode, jobNodeIds);

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
            s.Id, s.Label, s.Kind, degree.GetValueOrDefault(s.Id), hotspotIds.Contains(s.Id), s.Content)).ToList();

        return DecisionTree.Of(nodes, edges);
    }

    /// <summary>
    /// Adds one <see cref="TreeNodeKind.JobRunOutput"/> node per embedded run output (hung off its job
    /// when <see cref="RunOutputNode.JobId"/> resolves to a job node, otherwise off the root),
    /// then links each to its top-<see cref="MaxSimilarLinks"/> nearest peers above
    /// <see cref="SimilarityThreshold"/> cosine similarity. Undirected — symmetric pairs are de-duplicated.
    /// </summary>
    private static void WeaveRunOutputs(
        IReadOnlyList<RunOutputNode>? runOutputs,
        string rootId,
        List<DecisionTreeEdge> edges,
        HashSet<string> seen,
        Action<string, string, TreeNodeKind, string?> addNode,
        IReadOnlyDictionary<Guid, string>? jobNodeIds = null)
    {
        if (runOutputs is not { Count: > 0 }) return;

        var outputs = new List<(string Id, float[] Vector)>();
        foreach (var o in runOutputs)
        {
            if (o.Vector.Count == 0) continue;
            var id = "runoutput:" + o.Id;
            if (!seen.Contains(id))
            {
                addNode(id, Clip(CleanLabel(o.Label, id)), TreeNodeKind.JobRunOutput, o.Label);
                var parentId = o.JobId is not null && jobNodeIds is not null && jobNodeIds.TryGetValue(o.JobId.Value, out var jobNodeId)
                    ? jobNodeId
                    : rootId;
                edges.Add(new DecisionTreeEdge(parentId, id, ConfidenceTag.Extracted));
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

    // Content is a readable snippet, not the whole document — keep it to a paragraph.
    private static string? ClipContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var t = content.Trim();
        return t.Length <= MaxContent ? t : t[..(MaxContent - 1)] + "…";
    }
}
