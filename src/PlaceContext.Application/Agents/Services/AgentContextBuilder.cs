using System.Text;
using System.Text.RegularExpressions;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds the RAG context for a chat message: direct mention lookup (addresses, quoted names →
/// artifacts + entity records), semantic search over run outputs and the universal content index,
/// and a summary of the project's dependency graph.
/// </summary>
public sealed class AgentContextBuilder
{
    private readonly IEmbeddingGateway? _embeddings;
    private readonly IRunEmbeddingRepository? _embeddingStore;
    private readonly IContentIndexer? _contentIndexer;
    private readonly IDecisionTreeProvider? _treeProvider;
    private readonly IRunArtifactLinkRepository? _artifacts;
    private readonly IDataEntityRepository? _entities;
    private readonly IProjectDataStore? _projectData;

    public AgentContextBuilder(
        IEmbeddingGateway? embeddings = null,
        IRunEmbeddingRepository? embeddingStore = null,
        IContentIndexer? contentIndexer = null,
        IDecisionTreeProvider? treeProvider = null,
        IRunArtifactLinkRepository? artifacts = null,
        IDataEntityRepository? entities = null,
        IProjectDataStore? projectData = null)
    {
        _embeddings = embeddings;
        _embeddingStore = embeddingStore;
        _contentIndexer = contentIndexer;
        _treeProvider = treeProvider;
        _artifacts = artifacts;
        _entities = entities;
        _projectData = projectData;
    }

    /// <summary>
    /// Builds a context block to inject into the system prompt. Returns an empty string when
    /// no embeddings or graph data is available (the handler still works — just without RAG grounding).
    /// </summary>
    public async Task<string> BuildContextAsync(Guid projectId, string userMessage, int maxChunks, CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // 0. Direct mention lookup: addresses / quoted names in the question → artifacts and
        //    entity records that mention them. Deterministic complement to semantic search —
        //    exact address matches are where embeddings are weakest.
        await AppendDirectMatchesAsync(projectId, userMessage, sb, ct);

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

        // 2. Semantic search over the universal content index (documents, project data,
        //    decisions, activity, etc. — run outputs are already covered above).
        if (_contentIndexer is { IsEnabled: true })
        {
            try
            {
                var hits = await _contentIndexer.SearchAsync(
                    projectId, userMessage, maxChunks, kind: null, ct);
                if (hits.Count > 0)
                {
                    sb.AppendLine("## Related project content (semantically relevant)");
                    sb.AppendLine();
                    foreach (var h in hits)
                    {
                        sb.AppendLine($"- **{h.SourceKey}** ({h.Kind}): {Truncate(h.Text, 500)}");
                        sb.AppendLine();
                    }
                }
            }
            catch
            {
                // Best-effort: content search failure should not block the chat.
            }
        }

        // 3. Dependency graph — structured context.
        if (_treeProvider is not null)
        {
            try
            {
                var tree = await _treeProvider.BuildAsync(ProjectId.From(projectId), ct);
                if (tree.Nodes.Count > 0)
                {
                    sb.AppendLine("## Project dependency graph");
                    sb.AppendLine();

                    // Hotspots first.
                    var hotspots = tree.Nodes.Where(n => n.IsHotspot).ToList();
                    if (hotspots.Count > 0)
                    {
                        sb.AppendLine("Hotspots (high degree, most touched):");
                        foreach (var h in hotspots.Take(10))
                            sb.AppendLine($"- {h.Label} (degree: {h.Degree})");
                        sb.AppendLine();
                    }

                    // Full edge list.
                    var labels = tree.Nodes.ToDictionary(n => n.Id, n => n.Label);
                    sb.AppendLine("All relationships:");
                    foreach (var edge in tree.Edges)
                    {
                        var parent = labels.GetValueOrDefault(edge.ParentId) ?? "?";
                        var child = labels.GetValueOrDefault(edge.ChildId) ?? "?";
                        sb.AppendLine($"- {parent} → {child} ({edge.Confidence})");
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

    // Street addresses: number + words + road-type suffix (+ optional ", Suburb"). The search
    // uses the core address only (through the suffix) — suburbs vary too much in source data.
    private static readonly Regex AddressPattern = new(
        @"\b\d+[A-Za-z]?\s+(?:[A-Z][A-Za-z0-9']+\s+){1,4}(?:Street|St|Road|Rd|Avenue|Ave|Drive|Dr|Lane|Ln|Parade|Pde|Court|Ct|Place|Pl|Terrace|Tce|Way|Circuit|Cct|Boulevard|Blvd|Crescent|Cres|Close|Highway|Hwy|Esplanade)\b",
        RegexOptions.Compiled);

    private static readonly Regex QuotedPattern = new(
        "\"([^\"]{3,80})\"|‘([^’]{3,80})’",
        RegexOptions.Compiled);

    /// <summary>Pulls lookup terms out of a user message: street addresses and quoted names.
    /// Capped at 3 terms so a chatty message doesn't fan out into many lookups.</summary>
    internal static List<string> ExtractMentionTerms(string message)
    {
        var terms = new List<string>();
        if (string.IsNullOrWhiteSpace(message)) return terms;

        foreach (Match m in AddressPattern.Matches(message))
        {
            var term = m.Value.Trim();
            if (term.Length >= 8) terms.Add(term);
        }
        foreach (Match m in QuotedPattern.Matches(message))
        {
            var term = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            if (term.Length >= 3) terms.Add(term);
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
    }

    /// <summary>Direct (non-semantic) retrieval: artifacts whose title mentions a lookup term,
    /// and records in entity tables that match it — so "Tell me about 20 Balfour Street, Darra"
    /// pulls the right document and rows even before/alongside embedding search.</summary>
    private async Task AppendDirectMatchesAsync(Guid projectId, string userMessage, StringBuilder sb, CancellationToken ct)
    {
        var terms = ExtractMentionTerms(userMessage);
        if (terms.Count == 0) return;

        var section = new StringBuilder();
        var seenArtifacts = new HashSet<Guid>();
        var seenRecords = new HashSet<string>(StringComparer.Ordinal);

        // Artifacts whose title/kind mentions the term (exact, case-insensitive).
        if (_artifacts is not null)
        {
            foreach (var term in terms)
            {
                try
                {
                    var matches = await _artifacts.ListForProjectAsync(projectId, 5, term, ct);
                    foreach (var a in matches)
                    {
                        if (!seenArtifacts.Add(a.Id)) continue;
                        section.AppendLine($"- **{a.Title}** ({a.Kind}, {a.CreatedAt:yyyy-MM-dd}) — id:{a.Id} — call [[tool:show_artifact|{a.Id}]] to read its content.");
                    }
                }
                catch { /* best-effort */ }
            }
        }

        // Records in entity tables that mention the term.
        if (_entities is not null && _projectData is not null)
        {
            IReadOnlyList<DataEntity> entities;
            try { entities = await _entities.ListForProjectAsync(projectId, ct); }
            catch { entities = Array.Empty<DataEntity>(); }

            foreach (var entity in entities.Take(3))
            {
                foreach (var term in terms)
                {
                    try
                    {
                        var page = await _projectData.QueryTablePageAsync(projectId, entity.TableName, term, 1, 3, ct: ct);
                        foreach (var row in page.Rows)
                        {
                            var fields = string.Join(", ", page.Columns.Take(5)
                                .Select((c, i) => $"{c}={(i < row.Count ? row[i] : null)}"));
                            if (seenRecords.Add(entity.Name + "|" + fields))
                                section.AppendLine($"- **{entity.Name}** record: {fields}");
                        }
                    }
                    catch { /* best-effort — some tables may not be queryable */ }
                }
            }
        }

        if (section.Length > 0)
        {
            sb.AppendLine("## Direct matches in project data");
            sb.AppendLine();
            sb.Append(section);
            sb.AppendLine();
        }
    }
}
