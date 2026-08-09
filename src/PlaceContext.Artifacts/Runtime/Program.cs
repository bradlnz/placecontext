using System.Threading.RateLimiting;
using PlaceContext.BuildingBlocks;
using PlaceContext.ServiceDefaults;
using PlaceContext.Artifacts;
using PlaceContext.Artifacts.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlaceContextCqrs();
builder.Services.AddArtifactsModule();
builder.Services.AddArtifactsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(ArtifactsController).Assembly);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("artifact-share", context =>
    {
        var partition = $"{context.Request.Host.Host}:{context.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});

var app = builder.Build();
app.UseRateLimiter();
app.UsePlaceContextServiceRuntime("artifacts");
app.Run();

public partial class Program;
