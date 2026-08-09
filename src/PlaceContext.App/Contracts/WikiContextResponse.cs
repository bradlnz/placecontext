namespace PlaceContext.App.Controllers;

public sealed record WikiContextResponse(IReadOnlyList<WikiArticleSummaryResponse> Articles, WikiArticleResponse? Article);
