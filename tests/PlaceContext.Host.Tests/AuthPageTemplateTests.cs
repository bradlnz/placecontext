using Microsoft.AspNetCore.Antiforgery;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Tests;

public sealed class AuthPageTemplateTests
{
    [Fact]
    public void Published_auth_templates_load_and_render_encoded_form_values()
    {
        var tokens = new AntiforgeryTokenSet("request-token", "cookie-token", "af", "header");
        var join = AuthPages.Join(tokens, "<invite>", new InviteInfo("a@example.test", "Member"), "<error>");
        var verify = AuthPages.EmailVerify(tokens, "<state>",
            new EmailVerifyModel("Verify", "a***@example.test", false, null, null), "<error>");

        foreach (var page in new[] { AuthPages.Locked(), AuthPages.JoinInvalid(), join, verify })
        {
            Assert.Contains("<html", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", page);
        }
        Assert.Contains("&lt;invite&gt;", join);
        Assert.Contains("&lt;state&gt;", verify);
        Assert.Contains("&lt;error&gt;", verify);
        Assert.Contains("request-token", join);
        Assert.Contains("request-token", verify);
    }
}
