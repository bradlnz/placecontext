using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Artifacts;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataEntities;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SearchHandler : IQueryHandler<SearchQuery, SearchResultsView>
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IDecisionRepository _decisions;
    private readonly IRunArtifactLinkRepository? _artifacts;
    private readonly IEntityTagStore? _tagIndex;
    private readonly IContentIndexer? _contentIndex;
    private readonly ISearchDataGateway? _openSearch;
    private readonly IPermissionService? _permissions;
    private readonly IJobChainRepository? _workflows;
    private readonly IJobRepository? _jobs;
    private readonly IProjectAgentStore? _agents;
    private readonly IBuildChatStore? _dashboard;

    public SearchHandler(
        IProjectRepository projects, IActivityLogRepository ledgers,
        IDecisionRepository decisions,
        // Optional so existing tests construct the handler unchanged; DI always supplies it.
        IRunArtifactLinkRepository? artifacts = null,
        IEntityTagStore? tagIndex = null,
        IContentIndexer? contentIndex = null,
        ISearchDataGateway? openSearch = null,
        IPermissionService? permissions = null,
        IJobChainRepository? workflows = null,
        IJobRepository? jobs = null,
        IProjectAgentStore? agents = null,
        IBuildChatStore? dashboard = null)
    {
        _projects = projects;
        _ledgers = ledgers;
        _decisions = decisions;
        _artifacts = artifacts;
        _tagIndex = tagIndex;
        _contentIndex = contentIndex;
        _openSearch = openSearch;
        _permissions = permissions;
        _workflows = workflows;
        _jobs = jobs;
        _agents = agents;
        _dashboard = dashboard;
    }

    public async Task<SearchResultsView> HandleAsync(SearchQuery query, CancellationToken ct = default)
    {
        var term = (query.Term ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return new SearchResultsView(term, Array.Empty<SearchHit>());
        }
        bool Match(string? s) => s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);

        var hits = new List<SearchHit>();
        var projects = await _projects.ListAsync(ct);
        var canViewWorkflows = _permissions is not null && await _permissions.HasAsync(Permission.ChainsManage, ct);
        var canViewJobs = _permissions is not null && await _permissions.HasAsync(Permission.JobsView, ct);
        var canViewAgents = _permissions is not null && await _permissions.HasAsync(Permission.ProjectsView, ct);
        foreach (var project in projects.Where(p => query.ProjectId is null || p.Id.Value == query.ProjectId))
        {
            var projectId = project.Id.Value;
            var projectName = project.Name.Value;
            if (canViewWorkflows && _workflows is not null)
                foreach (var workflow in (await _workflows.ListForProjectAsync(projectId, ct))
                    .Where(w => Match(w.Name) || Match(w.Description)).Take(8))
                    hits.Add(new SearchHit("workflow", projectId, workflow.Name, projectName,
                        $"/project/{projectId}/workflows/{workflow.Id}"));
            if (canViewJobs && _jobs is not null)
                foreach (var job in (await _jobs.ListForProjectAsync(projectId, ct))
                    .Where(j => Match(j.Name) || Match(j.Description)).Take(8))
                    hits.Add(new SearchHit("job", projectId, job.Name, projectName,
                        $"/project/{projectId}/jobs?job={job.Id}"));
            if (canViewAgents)
            {
                var shared = _agents is null ? [] : (await _agents.GetAsync(projectId, ct)).Agents;
                var personal = _dashboard is null ? [] : (await _dashboard.GetSettingsAsync(projectId, ct)).Agents;
                foreach (var agent in shared.Concat(personal).Where(a => Match(a.DisplayName)).Take(8))
                    hits.Add(new SearchHit("agent", projectId, agent.DisplayName, projectName,
                        $"/project/{projectId}/agents/{agent.Id}"));
            }
        }

        // One bounded OpenSearch request for the active project. Keep it best-effort so a missing
        // connection cannot break workspace search, and permission-check it independently so users
        // without data.read still receive the ordinary project/change results.
        if (query.ProjectId is { } openSearchProjectId
            && _openSearch is not null
            && (_permissions is null || await _permissions.HasAsync(Permission.DataRead, ct)))
        {
            try
            {
                var result = await _openSearch.SearchAsync(new SearchResultRequest(
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

        // The tag index: every tagged piece of data is searchable, answered as graph nodes —
        // hits land on the record inside its entity's business view.
        if (_tagIndex is not null)
        {
            foreach (var t in await _tagIndex.SearchKeysAsync(term, 8, ct))
            {
                hits.Add(new SearchHit("entity", t.ProjectId, t.Key, $"{t.EntityName} · data graph",
                                $"/project/{t.ProjectId}/entity/{Uri.EscapeDataString(t.EntityName)}?record={Uri.EscapeDataString(t.Key)}"));
            }
        }

        // Stored run artifacts: title/kind matches open straight in the file viewer's stream URL.
        if (_artifacts is not null)
        {
            foreach (var a in (await _artifacts.ListRecentAsync(300, ct))
                     .Where(a => Match(a.Title) || Match(a.Kind.ToString()) || Match(a.ContentType)).Take(8))
            {
                hits.Add(new SearchHit("artifact", a.ProjectId, a.Title,
                                $"{a.Kind} · {a.ContentType}", $"/artifacts?artifact={a.Id}"));
            }
        }

        foreach (var p in projects)
        {
            var url = $"/project/{p.Id.Value}";
            var name = p.Name.Value;

            if (Match(name) || Match(p.Path.Value))
            {
                hits.Add(new SearchHit("project", p.Id.Value, name, p.Path.Value, url));
            }
            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            foreach (var r in ledger.Records.Where(r => Match(r.Summary)).Take(5))
            {
                hits.Add(new SearchHit("change", p.Id.Value, r.Summary, $"{name} · change #{r.Sequence}", $"{url}#changes"));
            }
            foreach (var d in (await _decisions.ListForProjectAsync(p.Id, ct)).Where(d => Match(d.Question) || Match(d.Choice)).Take(3))
            {
                hits.Add(new SearchHit("decision", p.Id.Value, d.Question, $"{name} · {d.Choice}", $"{url}#decision-{d.Id.Value}"));
            }
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

        return new SearchResultsView(term, hits.Take(query.Limit).ToList());
    }

    private static string? FirstField(SearchHitView hit, params string[] names)
    {
        foreach (var name in names)
        {
            var value = hit.Fields.FirstOrDefault(field =>
                field.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                || field.Key.EndsWith('.' + name, StringComparison.OrdinalIgnoreCase)).Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Short(value);
            }
        }
        return null;
    }

    private static Guid? FindArtifactId(SearchHitView hit)
    {
        foreach (var field in hit.Fields)
        {
            var leaf = field.Key.Split('.').Last()
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal);
            if (!leaf.Equals("artifactid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (Guid.TryParse(field.Value, out var artifactId))
            {
                return artifactId;
            }
        }
        return null;
    }

    private static string? FirstOtherField(SearchHitView hit, string title, params string[] names)
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
            ContentKind.Requirements => $"/project/{projectId}?tab=requirements",
            _ => $"/project/{projectId}"
        };
}
