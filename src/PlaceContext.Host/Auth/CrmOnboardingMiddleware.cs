using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Auth;

public sealed class CrmOnboardingMiddleware
{
    private readonly RequestDelegate _next;

    public CrmOnboardingMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ICrmUserRepository crmUsers, IClock clock)
    {
        if (!ShouldValidate(context))
        {
            await _next(context);
            return;
        }

        var code = NormalizeCode(await ReadCodeAsync(context));
        if (string.IsNullOrWhiteSpace(code))
        {
            context.Response.Redirect("/joininvalid");
            return;
        }

        if (!CrmUser.IsJoinCodeFormatValid(code))
        {
            context.Response.Redirect("/joininvalid");
            return;
        }

        var user = await crmUsers.GetByJoinCodeAsync(code!.Trim(), clock.UtcNow, context.RequestAborted);
        if (user is null)
        {
            context.Response.Redirect("/joininvalid");
            return;
        }

        await _next(context);
    }

    private static bool ShouldValidate(HttpContext context)
    {
        if (context.Request.Method is not ("GET" or "POST"))
            return false;

        var path = context.Request.Path;
        return context.Request.Method == HttpMethods.Get && path.Equals("/crm/onboarding", StringComparison.OrdinalIgnoreCase)
            || context.Request.Method == HttpMethods.Post && path.Equals("/auth/complete-crm-onboarding", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadCodeAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethods.Get)
            return context.Request.Query["code"].ToString();

        if (!context.Request.HasFormContentType)
            return null;

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        return form["code"].ToString();
    }

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();
}
