using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SearchHandler : IQueryHandler<SearchQuery, SearchResultsView>
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IProjectContextRepository _contexts;
    private readonly IDecisionRepository _decisions;

    public SearchHandler(
        IProjectRepository projects, IActivityLogRepository ledgers,
        IProjectContextRepository contexts, IDecisionRepository decisions)
    {
        _projects = projects;
        _ledgers = ledgers;
        _contexts = contexts;
        _decisions = decisions;
    }

    public async Task<SearchResultsView> HandleAsync(SearchQuery query, CancellationToken ct = default)
    {
        var term = (query.Term ?? string.Empty).Trim();
        if (term.Length < 2)
            return new SearchResultsView(term, Array.Empty<SearchHit>());

        bool Match(string? s) => s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);

        var hits = new List<SearchHit>();
        foreach (var p in await _projects.ListAsync(ct))
        {
            var url = $"/project/{p.Id.Value}";
            var name = p.Name.Value;

            if (Match(name) || Match(p.Path.Value))
                hits.Add(new SearchHit("project", p.Id.Value, name, p.Path.Value, url));

            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            foreach (var r in ledger.Records.Where(r => Match(r.Summary)).Take(5))
                hits.Add(new SearchHit("change", p.Id.Value, r.Summary, $"{name} · change #{r.Sequence}", $"{url}#changes"));

            var context = await _contexts.GetForProjectAsync(p.Id, ct);
            if (context is not null && Match(context.Markdown))
                hits.Add(new SearchHit("context", p.Id.Value, Snippet(context.Markdown, term), $"{name} · context", $"{url}#context"));

            foreach (var d in (await _decisions.ListForProjectAsync(p.Id, ct)).Where(d => Match(d.Question) || Match(d.Choice)).Take(3))
                hits.Add(new SearchHit("decision", p.Id.Value, d.Question, $"{name} · {d.Choice}", $"{url}#decision-{d.Id.Value}"));
        }

        return new SearchResultsView(term, hits.Take(query.Limit).ToList());
    }

    /// <summary>A short window of text around the first match, for context hits.</summary>
    private static string Snippet(string text, string term)
    {
        var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return text.Length <= 100 ? text : text[..100];
        var start = Math.Max(0, i - 40);
        var len = Math.Min(text.Length - start, 100);
        var s = text.Substring(start, len).Replace('\n', ' ').Trim();
        return (start > 0 ? "…" : "") + s + (start + len < text.Length ? "…" : "");
    }
}
