using System.Text;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds the RAG context for a chat message: retrieves top-k semantically similar run outputs
/// via the embedding/search layer, related project data via the universal content index,
/// and a summary of the project's dependency graph.
/// </summary>
public sealed class AgentContextBuilder
{
    private readonly IEmbeddingGateway? _embeddings;
    private readonly IRunEmbeddingRepository? _embeddingStore;
    private readonly IContentIndexer? _contentIndexer;
    private readonly IDecisionTreeProvider? _treeProvider;

    public AgentContextBuilder(
        IEmbeddingGateway? embeddings = null,
        IRunEmbeddingRepository? embeddingStore = null,
        IContentIndexer? contentIndexer = null,
        IDecisionTreeProvider? treeProvider = null)
    {
        _embeddings = embeddings;
        _embeddingStore = embeddingStore;
        _contentIndexer = contentIndexer;
        _treeProvider = treeProvider;
    }

    /// <summary>
    /// Builds a context block to inject into the system prompt. Returns an empty string when
    /// no embeddings or graph data is available (the handler still works — just without RAG grounding).
    /// </summary>
    public async Task<string> BuildContextAsync(Guid projectId, string userMessage, int maxChunks, CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // 1. Semantic search over run outputs.
        if (_embeddings is { IsEnabled: true } && _embeddingStore is not null)
        {
            try
            {
                var vectors = await _embeddings.EmbedAsync(new[] { userMessage }, ct);
                if (vectors.Count > 0)
                {
                    var matches = await _embeddingStore.SearchAsync(projectId, vectors[0], maxChunks, ct);
                    if (matches.Count > 0)
                    {
                        sb.AppendLine("## Recent run outputs (semantically relevant)");
                        sb.AppendLine();
                        foreach (var m in matches)
                        {
                            sb.AppendLine($"- **run {m.Embedding.JobRunId.ToString()[..8]}** (job {m.Embedding.JobId.ToString()[..8]}): {Truncate(m.Embedding.Text, 500)}");
                            sb.AppendLine();
                        }
                    }
                }
            }
            catch
            {
                // Best-effort: embedding search failure should not block the chat.
            }
        }

        // 2. Semantic search over the universal content index (project data only —
        //    run outputs are already covered above).
        if (_contentIndexer is { IsEnabled: true })
        {
            try
            {
                var hits = await _contentIndexer.SearchAsync(
                    projectId, userMessage, maxChunks, ContentKind.ProjectData, ct);
                if (hits.Count > 0)
                {
                    sb.AppendLine("## Related project data (semantically relevant)");
                    sb.AppendLine();
                    foreach (var h in hits)
                    {
                        sb.AppendLine($"- **{h.SourceKey}**: {Truncate(h.Text, 500)}");
                        sb.AppendLine();
                    }
                }
            }
            catch
            {
                // Best-effort: content search failure should not block the chat.
            }
        }

        // 3. Dependency graph summary.
        if (_treeProvider is not null)
        {
            try
            {
                var tree = await _treeProvider.BuildAsync(ProjectId.From(projectId), ct);
                if (tree.Nodes.Count > 0)
                {
                    sb.AppendLine("## Project dependency graph");
                    sb.AppendLine();
                    sb.AppendLine(tree.Answer("summary"));
                    sb.AppendLine();

                    var topNodes = tree.Nodes
                        .OrderByDescending(n => n.Degree)
                        .Take(maxChunks)
                        .ToList();
                    sb.AppendLine("Top nodes:");
                    foreach (var node in topNodes)
                    {
                        sb.AppendLine($"- {node.Label} (touches: {node.Degree}, hotspot: {node.IsHotspot})");
                    }

                    var labels = tree.Nodes.ToDictionary(n => n.Id, n => n.Label);
                    var topEdges = tree.Edges.Take(maxChunks).ToList();
                    if (topEdges.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("Key relationships:");
                        foreach (var edge in topEdges)
                        {
                            var parent = labels.GetValueOrDefault(edge.ParentId) ?? edge.ParentId;
                            var child = labels.GetValueOrDefault(edge.ChildId) ?? edge.ChildId;
                            sb.AppendLine($"- {parent} → {child} ({edge.Confidence})");
                        }
                    }
                }
            }
            catch
            {
                // Best-effort: graph build failure should not block the chat.
            }
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";
}
