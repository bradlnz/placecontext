using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api;

namespace PlaceContext.Host.Tests;

public sealed class AgentStreamAuthorizationContractTests
{
    [Fact]
    public void Stream_is_generic_and_requires_a_real_user_identity()
    {
        var route = typeof(AgentStreamController).GetCustomAttribute<RouteAttribute>();
        Assert.Equal("api/v1/agent", route?.Template);

        var auth = typeof(AgentStreamController).GetCustomAttributes<AuthorizeAttribute>().ToArray();
        var schemes = Assert.Single(auth, a => !string.IsNullOrEmpty(a.AuthenticationSchemes));
        Assert.Equal(AgentAuthenticationDefaults.SchemeName, schemes.AuthenticationSchemes);
        Assert.DoesNotContain(ApiKeyAuthenticationHandler.SchemeName, schemes.AuthenticationSchemes);
        Assert.Contains(auth, a => a.Policy == Permission.AgentsChat);
        Assert.DoesNotContain(auth, a => a.Policy == Permission.DataRead);
    }

    [Theory]
    [InlineData("Bearer pct_abc", null, UserApiTokenAuthenticationHandler.SchemeName)]
    [InlineData(null, "pct_abc", UserApiTokenAuthenticationHandler.SchemeName)]
    [InlineData("Bearer eyJhbGciOiJSUzI1NiJ9.payload.signature", null, "Bearer")]
    public void Credential_selector_uses_exactly_one_validator(
        string? authorization, string? apiKey, string expected)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        if (apiKey is not null) context.Request.Headers["X-Api-Key"] = apiKey;

        Assert.Equal(expected, AgentAuthenticationDefaults.SelectScheme(context));
    }

    [Fact]
    public void Input_contract_is_reusable_and_carries_no_ossen_domain_fields()
    {
        var names = typeof(AgentStreamRequest).GetProperties().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "Message", "Context", "CorrelationId" }, names);
    }
}
