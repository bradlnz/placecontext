namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record WikiArticleResponse(string Slug, string Title, string Summary, string Html);
