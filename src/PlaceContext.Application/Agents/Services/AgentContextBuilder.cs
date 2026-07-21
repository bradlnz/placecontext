using System.Text;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds the RAG context for a chat message: retrieves top-k semantically similar run outputs
/// via the embedding/search layer, and a summary of the project's dependency graph.
/// </summary>
public sealed class AgentContextBuilder
{
    private readonly IEmbeddingGateway? _embeddings;
    private readonly IRunEmbeddingRepository? _embeddingStore;
    private readonly IDecisionTreeProvider? _treeProvider;

    public AgentContextBuilder(
        IEmbeddingGateway? embeddings = null,
        IRunEmbeddingRepository? embeddingStore = null,
        IDecisionTreeProvider? treeProvider = null)
    {
        _embeddings = embeddings;
        _embeddingStore = embeddingStore;
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

        // 2. Dependency graph summary.
        if (_treeProvider is not null)
        {
            try
            {
                var tree = await _treeProvider.BuildAsync(ProjectId.From(projectId), ct);
                if (tree.Nodes.Count > 0)
                {
                    sb.AppendLine("## Project structure");
                    sb.AppendLine();
                    var topNodes = tree.Nodes
                        .OrderByDescending(n => n.Degree)
                        .Take(15)
                        .ToList();
                    foreach (var node in topNodes)
                    {
                        sb.AppendLine($"- {node.Label} (touches: {node.Degree}, hotspot: {node.IsHotspot})");
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
