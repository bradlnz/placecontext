using PlaceContext.BuildingBlocks;
using PlaceContext.ServiceDefaults;
using PlaceContext.Crm;
using PlaceContext.Crm.Controllers;
using PlaceContext.Crm.Infrastructure.Persistence;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlaceContextCqrs();
builder.Services.AddCrmModule();
builder.Services.AddCrmInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(CrmController).Assembly);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-ingestion", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();
await app.Services.MigrateCrmDatabaseAsync();
await PlaceContext.Crm.Infrastructure.Security.CrmEncryptionAtRestBootstrap.RunAsync(app.Services);
app.UseRateLimiter();
app.UsePlaceContextServiceRuntime("crm");
app.Run();

public partial class Program;
