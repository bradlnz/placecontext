using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Net.Http.Headers;
using PlaceContext.Application;
using PlaceContext.ServiceDefaults;
using PlaceContext.Identity;
using PlaceContext.Identity.Controllers;
using PlaceContext.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddIdentityModule();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(
    builder.Configuration,
    typeof(IdentityServiceController).Assembly);
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "IdentitySmartAuthentication";
        options.DefaultChallengeScheme = "IdentitySmartAuthentication";
    })
    .AddPolicyScheme("IdentitySmartAuthentication", null, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers[HeaderNames.Authorization].ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? ServiceAuthenticationDefaults.Scheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
    });

var app = builder.Build();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<ServiceRequestContextMiddleware>();
app.UseMiddleware<IdentityTenantResolutionMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Ok(new { service = "identity", status = "ready" }))
    .AllowAnonymous();
app.Run();

public partial class Program;
