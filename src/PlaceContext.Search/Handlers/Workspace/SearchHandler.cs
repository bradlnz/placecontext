using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SearchHandler : IQueryHandler<SearchQuery, SearchResultsView>
{
    private readonly IProjectRepository? _projects;
    private readonly IActivityLogRepository? _ledgers;
    private readonly IDecisionRepository? _decisions;
    private readonly IContentIndexer? _contentIndex;
    private readonly IOpenSearchDataGateway? _openSearch;
    private readonly IPermissionService? _permissions;

    public SearchHandler(
        IProjectRepository? projects = null,
        IActivityLogRepository? ledgers = null,
        IDecisionRepository? decisions = null,
        IContentIndexer? contentIndex = null,
        IOpenSearchDataGateway? openSearch = null,
        IPermissionService? permissions = null)
    {
        _projects = projects;
        _ledgers = ledgers;
        _decisions = decisions;
        _contentIndex = contentIndex;
        _openSearch = openSearch;
        _permissions = permissions;
    }

    public async Task<SearchResultsView> HandleAsync(SearchQuery query, CancellationToken ct = default)
    {
        var term = (query.Term ?? string.Empty).Trim();
        if (term.Length < 2)
            return new SearchResultsView(term, Array.Empty<SearchHit>());

        bool Match(string? s) => s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);

        var hits = new List<SearchHit>();

        // One bounded OpenSearch request for the active project. Keep it best-effort so a missing
        // connection cannot break workspace search, and permission-check it independently so users
        // without data.read still receive the ordinary project/change results.
        if (query.ProjectId is { } openSearchProjectId
            && _openSearch is not null
            && (_permissions is null || await _permissions.HasAsync(Permission.DataRead, ct)))
        {
            try
            {
                var result = await _openSearch.SearchAsync(new OpenSearchSearchRequest(
                    openSearchProjectId, "*", term, PageSize: 8), ct);
                foreach (var hit in result.Hits)
                {
                    var title = FirstField(hit, "full_address", "site_address", "property_address",
                        "address", "official_name", "display_name", "name", "title")
                        ?? $"{hit.Index} document";
                    var detail = FirstOtherField(hit, title, "suburb", "locality", "council",
                        "authority", "description", "status", "type");
                    var subtitle = detail is null ? hit.Index : $"{hit.Index} · {detail}";
                    var artifactId = FindArtifactId(hit);
                    var url = artifactId is { } id
                        ? $"/artifacts?artifact={id}"
                        : $"/project/{openSearchProjectId}/data-search"
                          + $"?index={Uri.EscapeDataString(hit.Index)}"
                          + $"&q={Uri.EscapeDataString(term)}"
                          + $"&document={Uri.EscapeDataString(hit.Id)}";
                    hits.Add(new SearchHit("opensearch", openSearchProjectId, title, subtitle, url));
                }
            }
            catch
            {
                // Search remains useful when OpenSearch is unavailable or not configured.
            }
        }

        if (_projects is not null && _ledgers is not null && _decisions is not null)
        {
            foreach (var p in await _projects.ListAsync(ct))
            {
                var url = $"/project/{p.Id.Value}";
                var name = p.Name.Value;

                if (Match(name) || Match(p.Path.Value))
                    hits.Add(new SearchHit("project", p.Id.Value, name, p.Path.Value, url));

                var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
                foreach (var r in ledger.Records.Where(r => Match(r.Summary)).Take(5))
                    hits.Add(new SearchHit("change", p.Id.Value, r.Summary, $"{name} · change #{r.Sequence}", $"{url}#changes"));

                foreach (var d in (await _decisions.ListForProjectAsync(p.Id, ct)).Where(d => Match(d.Question) || Match(d.Choice)).Take(3))
                    hits.Add(new SearchHit("decision", p.Id.Value, d.Question, $"{name} · {d.Choice}", $"{url}#decision-{d.Id.Value}"));

                // Universal content index: semantic search over project data, decisions, activity, charts, etc.
                if (_contentIndex is { IsEnabled: true })
                {
                    foreach (var c in await _contentIndex.SearchAsync(p.Id.Value, term, take: 5, ct: ct))
                    {
                        var subtitle = c.Text.Length <= 120 ? c.Text : c.Text[..120] + "…";
                        hits.Add(new SearchHit(c.Kind, p.Id.Value, c.SourceKey, subtitle, ContentUrl(p.Id.Value, c.Kind)));
                    }
                }
            }
        }

        return new SearchResultsView(term, hits.Take(query.Limit).ToList());
    }

    private static string? FirstField(OpenSearchHitView hit, params string[] names)
    {
        foreach (var name in names)
        {
            var value = hit.Fields.FirstOrDefault(field =>
                field.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                || field.Key.EndsWith('.' + name, StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrWhiteSpace(value)) return Short(value);
        }
        return null;
    }

    private static Guid? FindArtifactId(OpenSearchHitView hit)
    {
        foreach (var field in hit.Fields)
        {
            var leaf = field.Key.Split('.').Last()
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal);
            if (!leaf.Equals("artifactid", StringComparison.OrdinalIgnoreCase)) continue;
            if (Guid.TryParse(field.Value, out var artifactId)) return artifactId;
        }
        return null;
    }

    private static string? FirstOtherField(OpenSearchHitView hit, string title, params string[] names)
    {
        var value = FirstField(hit, names);
        return string.Equals(value, title, StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string Short(string value)
        => value.Length <= 120 ? value : value[..117] + "…";

    private static string ContentUrl(Guid projectId, string kind)
        => kind switch
        {
            ContentKind.Decision => $"/project/{projectId}#decisions",
            ContentKind.Activity => $"/project/{projectId}#changes",
            ContentKind.Event => $"/project/{projectId}/events",
            ContentKind.Chart => $"/project/{projectId}/analytics",
            ContentKind.ProjectData => $"/project/{projectId}/data",
            ContentKind.RunOutput => $"/project/{projectId}/jobs",
            ContentKind.Requirements => $"/project/{projectId}/agents",
            _ => $"/project/{projectId}"
        };
}
