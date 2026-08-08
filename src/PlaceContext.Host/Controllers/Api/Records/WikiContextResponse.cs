namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record WikiContextResponse(
    IReadOnlyList<WikiArticleSummaryResponse> Articles,
    WikiArticleResponse? Article);
