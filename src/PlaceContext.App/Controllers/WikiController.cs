using Microsoft.AspNetCore.Mvc;
using PlaceContext.App.Authentication;
using PlaceContext.App.Wiki;

namespace PlaceContext.App.Controllers;

[ApiController]
[Route("api/v1/wiki")]
[Produces("application/json")]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client)]
public sealed class WikiController(EdgeCallerContext caller) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WikiContextResponse>> Get([FromQuery] string? slug)
    {
        if (await caller.AuthenticateAsync(HttpContext) is null) return Unauthorized();

        var article = slug is null ? WikiLibrary.Articles.FirstOrDefault() : WikiLibrary.Find(slug);
        return Ok(new WikiContextResponse(
            WikiLibrary.Articles.Select(item => new WikiArticleSummaryResponse(
                item.Slug,
                item.Title,
                item.Summary)).ToList(),
            article is null
                ? null
                : new WikiArticleResponse(
                    article.Slug,
                    article.Title,
                    article.Summary,
                    article.Html)));
    }
}
