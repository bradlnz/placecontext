using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Host.Wiki;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/wiki")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Produces("application/json")]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client)]
public sealed class WikiController : ControllerBase
{
    [HttpGet]
    public ActionResult<WikiContextResponse> Get([FromQuery] string? slug)
    {
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
