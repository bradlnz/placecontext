using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api;

namespace PlaceContext.Host.Tests;

public sealed class SearchApiMapperTests
{
    [Fact]
    public void Search_endpoint_uses_entity_api_authentication_and_data_read_permission()
    {
        var controllerAuth = Assert.Single(
            typeof(SearchController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
        );
        Assert.Contains(
            UserApiTokenAuthenticationHandler.SchemeName,
            controllerAuth.AuthenticationSchemes
        );
        Assert.Contains(
            ApiKeyAuthenticationHandler.SchemeName,
            controllerAuth.AuthenticationSchemes
        );

        var method = typeof(SearchController).GetMethod(nameof(SearchController.Search))!;
        var route = Assert.Single(
            method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
        );
        Assert.NotNull(route);
        var methodAuth = Assert.Single(
            method
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
        );
        Assert.Equal(Permission.DataRead, methodAuth.Policy);
    }

    [Fact]
    public void Response_is_bounded_and_cannot_leak_hits_from_another_project()
    {
        var selected = Guid.NewGuid();
        var other = Guid.NewGuid();
        var results = new SearchResultsView(
            "customer",
            new[]
            {
                new SearchHit("artifact", selected, "First", "PDF", "/artifacts?artifact=1"),
                new SearchHit(
                    "decision",
                    other,
                    "Other tenant project",
                    "hidden",
                    "/project/other"
                ),
                new SearchHit("entity", selected, "Second", "Data", "/project/selected/entity"),
            }
        );

        var response = SearchApiMapper.ToResponse(results, selected, limit: 1);

        Assert.Equal(selected, response.ProjectId);
        Assert.Equal(1, response.Count);
        var hit = Assert.Single(response.Hits);
        Assert.Equal("First", hit.Title);
        Assert.Equal(selected, hit.ProjectId);
    }
}
