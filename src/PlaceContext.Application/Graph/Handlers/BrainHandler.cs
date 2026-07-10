using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds the organisation's centralised "brain" — one graph that joins every project's dependency
/// graph. Each project becomes a hub node connected to its god-nodes; god-nodes that share a filename
/// across projects are linked (inferred cross-project edges), so the per-project graphs become one.
/// Reuses the per-project <see cref="GraphVizView"/> shape so the same canvas renderer can draw it.
/// </summary>
public sealed class BrainHandler : IQueryHandler<GetBrainQuery, GraphVizView>
{
    private readonly IProjectRepository _projects;
    public BrainHandler(IProjectRepository projects) => _projects = projects;

    public async Task<GraphVizView> HandleAsync(GetBrainQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);
        var nodes = new List<GraphNodeView>();
        var links = new List<GraphLinkView>();
        var byFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in projects)
        {
            var hubId = $"proj:{p.Id.Value}";
            var gods = p.LastGraph?.GodNodes ?? (IReadOnlyList<GodNode>)Array.Empty<GodNode>();
            nodes.Add(new GraphNodeView(hubId, p.Name.Value, Math.Max(gods.Count, 1), IsGod: true));

            foreach (var g in gods)
            {
                var nid = $"{p.Id.Value}:{g.Id.Value}";
                nodes.Add(new GraphNodeView(nid, g.Label.Value, g.Degree, IsGod: false));
                links.Add(new GraphLinkView(hubId, nid, "Extracted"));

                var key = Basename(g.Label.Value);
                if (!byFile.TryGetValue(key, out var list)) byFile[key] = list = new();
                list.Add(nid);
            }
        }

        // Join the project graphs: chain god-nodes that share a filename across projects (inferred links).
        foreach (var ids in byFile.Values.Where(v => v.Count > 1))
            for (var i = 1; i < ids.Count; i++)
                links.Add(new GraphLinkView(ids[i - 1], ids[i], "Ambiguous"));

        return new GraphVizView(Guid.Empty, nodes.Count, links.Count, nodes, links);
    }

    private static string Basename(string s)
    {
        var i = s.LastIndexOf('/');
        return i >= 0 ? s[(i + 1)..] : s;
    }
}
