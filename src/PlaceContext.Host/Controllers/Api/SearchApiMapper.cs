using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Controllers.Api;

public static class SearchApiMapper
{
    public static SearchApiResponse ToResponse(SearchResultsView results, Guid projectId, int limit)
    {
        var hits = results.Hits
            .Where(hit => hit.ProjectId == projectId)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(hit => new SearchApiHitResponse(
                hit.Kind, hit.ProjectId, hit.Title, hit.Subtitle, hit.Url))
            .ToList();
        return new SearchApiResponse(results.Term, projectId, hits.Count, hits);
    }
}
