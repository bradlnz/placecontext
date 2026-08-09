using PlaceContext.App.Proxy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicroserviceProxy(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseStaticFiles();
// App is an edge proxy, not a microservice. The owning service validates bearer/API-key credentials
// or explicitly permits anonymous routes such as public ingestion and artifact shares.
app.UseMicroserviceProxy();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "app", status = "ready" }));
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html").AllowAnonymous();
await app.RunAsync();

public partial class Program;
