using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class McpOAuthStateTests
{
    [Theory]
    [InlineData("plaintext")]
    [InlineData("tampered")]
    [InlineData("expired")]
    public async Task Callback_rejects_unprotected_tampered_and_expired_state_before_token_exchange(string kind)
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector("mcp.oauth.state.v1").ToTimeLimitedDataProtector();
        const string payload = "{\"State\":\"state\",\"TokenEndpoint\":\"https://attacker.example\"}";
        var value = kind switch
        {
            "plaintext" => payload,
            "expired" => protector.Protect(payload, DateTimeOffset.UtcNow.AddMinutes(-1)),
            _ => protector.Protect(payload, TimeSpan.FromMinutes(5)) + "tampered"
        };
        var controller = new McpOAuthController(null!, provider, NullLogger<McpOAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers.Cookie = "mcp.oauth.state=" + Uri.EscapeDataString(value);
        var result = Assert.IsType<ContentResult>(await controller.Callback("code", "state", null));
        Assert.Contains("Invalid OAuth session", result.Content);
        Assert.DoesNotContain("success: true", result.Content);
    }
}
