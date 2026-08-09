namespace PlaceContext.App.Controllers;

public sealed record WikiArticleResponse(string Slug, string Title, string Summary, string Html);
