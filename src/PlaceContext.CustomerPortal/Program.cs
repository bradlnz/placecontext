using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure;
using PlaceContext.CustomerPortal;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<CustomerPortalOptions>()
    .Bind(builder.Configuration.GetSection("CustomerPortal"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});
app.UseMiddleware<CustomerPortalTenantMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/healthz");

app.Run();

public partial class Program { }
