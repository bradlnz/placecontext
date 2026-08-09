using System.Collections.Concurrent;
using System.Net;
using PlaceContext.Application.Dtos;
using Microsoft.AspNetCore.Antiforgery;

namespace PlaceContext.Identity.Auth;

/// <summary>
/// Renders the standalone login / register pages from HTML templates (<c>Auth/templates/*.html</c>),
/// injecting only the antiforgery token and any error text. Kept outside the Blazor render pipeline so
/// the form POST can set the auth cookie in a normal HTTP request (a Blazor circuit can't). No markup
/// lives in this file — the templates own the HTML and <c>wwwroot/auth.css</c> owns the styling.
/// </summary>
public static class AuthPages
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>The "open the portal from the TUI" page shown to unauthenticated visitors (no markup here —
    /// <c>Auth/templates/locked.html</c> owns it).</summary>
    public static string Locked() => Cache.GetOrAdd("locked", static name =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Auth", "templates", name + ".html")));

    public static string Join(AntiforgeryTokenSet tokens, string token, InviteInfo invite, string? error)
        => Render("join", tokens, error)
            .Replace("{{token}}", WebUtility.HtmlEncode(token))
            .Replace("{{email}}", WebUtility.HtmlEncode(invite.Email))
            .Replace("{{role}}", WebUtility.HtmlEncode(invite.Role));

    public static string JoinInvalid() => Cache.GetOrAdd("joininvalid", static name =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Auth", "templates", name + ".html")));

    public static string EmailVerify(
        AntiforgeryTokenSet tokens,
        string state,
        EmailVerifyModel model,
        string? error)
    {
        var html = Cache.GetOrAdd("email-verify", static name =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Auth", "templates", name + ".html")));
        return html
            .Replace("{{afName}}", tokens.FormFieldName)
            .Replace("{{afValue}}", tokens.RequestToken)
            .Replace("{{state}}", WebUtility.HtmlEncode(state))
            // {{heading}} is fixed copy; {{hint}} embeds an already-encoded masked destination.
            .Replace("{{heading}}", WebUtility.HtmlEncode(model.Heading))
            .Replace("{{hint}}", model.DestinationHint)
            .Replace("{{phoneClass}}", model.RequiresPhone ? "" : "hidden")
            .Replace("{{codeClass}}", model.RequiresPhone ? "hidden" : "")
            .Replace("{{switchClass}}", model.SwitchHref is null ? "hidden" : "")
            .Replace("{{switchHref}}", WebUtility.HtmlEncode(model.SwitchHref ?? string.Empty))
            .Replace("{{switchLabel}}", WebUtility.HtmlEncode(model.SwitchLabel ?? string.Empty))
            .Replace("{{errorClass}}", string.IsNullOrEmpty(error) ? "hidden" : "")
            .Replace("{{error}}", WebUtility.HtmlEncode(error ?? string.Empty));
    }

    private static string Render(string template, AntiforgeryTokenSet tokens, string? error, string? returnUrl = null)
    {
        var html = Cache.GetOrAdd(template, static name =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Auth", "templates", name + ".html")));

        // {{returnUrl}} → HTML-encoded value for the hidden form field; {{returnUrlHref}} → a URL-encoded
        // "?returnUrl=…" suffix for the login↔register cross-links (empty when there's nothing to carry).
        var hasReturn = !string.IsNullOrEmpty(returnUrl);
        return html
            .Replace("{{afName}}", tokens.FormFieldName)
            .Replace("{{afValue}}", tokens.RequestToken)
            .Replace("{{errorClass}}", string.IsNullOrEmpty(error) ? "hidden" : "")
            .Replace("{{error}}", WebUtility.HtmlEncode(error ?? string.Empty))
            .Replace("{{returnUrl}}", WebUtility.HtmlEncode(returnUrl ?? string.Empty))
            .Replace("{{returnUrlHref}}", hasReturn ? "?returnUrl=" + Uri.EscapeDataString(returnUrl!) : string.Empty);
    }
}
