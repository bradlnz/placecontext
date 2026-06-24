using System.Collections.Concurrent;
using System.Net;
using PlaceContext.Application.Dtos;
using Microsoft.AspNetCore.Antiforgery;

namespace PlaceContext.Host;

/// <summary>
/// Renders the standalone login / register pages from HTML templates (<c>Auth/templates/*.html</c>),
/// injecting only the antiforgery token and any error text. Kept outside the Blazor render pipeline so
/// the form POST can set the auth cookie in a normal HTTP request (a Blazor circuit can't). No markup
/// lives in this file — the templates own the HTML and <c>wwwroot/auth.css</c> owns the styling.
/// </summary>
public static class AuthPages
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Login(AntiforgeryTokenSet tokens, string? error) => Render("login", tokens, error);
    public static string Register(AntiforgeryTokenSet tokens, string? error) => Render("register", tokens, error);

    public static string Join(AntiforgeryTokenSet tokens, string token, InviteInfo invite, string? error)
        => Render("join", tokens, error)
            .Replace("{{token}}", WebUtility.HtmlEncode(token))
            .Replace("{{email}}", WebUtility.HtmlEncode(invite.Email))
            .Replace("{{role}}", WebUtility.HtmlEncode(invite.Role));

    public static string JoinInvalid() => Cache.GetOrAdd("joininvalid", static name =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Auth", "templates", name + ".html")));

    private static string Render(string template, AntiforgeryTokenSet tokens, string? error)
    {
        var html = Cache.GetOrAdd(template, static name =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Auth", "templates", name + ".html")));

        return html
            .Replace("{{afName}}", tokens.FormFieldName)
            .Replace("{{afValue}}", tokens.RequestToken)
            .Replace("{{errorClass}}", string.IsNullOrEmpty(error) ? "hidden" : "")
            .Replace("{{error}}", WebUtility.HtmlEncode(error ?? string.Empty));
    }
}
