using PlaceContext.Application.Runtime;
using PlaceContext.App.Proxy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddMicroserviceProxy(builder.Configuration);

var app = builder.Build();
app.UseStaticFiles();
// The owning service authorizes proxied requests. Dispatch first so explicitly anonymous service
// routes (for example public ingestion and artifact shares) are not blocked by App's fallback JWT.
app.UseMicroserviceProxy();
app.UsePlaceContextServiceRuntime("app");
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html").AllowAnonymous();
await app.RunAsync();

public partial class Program;
