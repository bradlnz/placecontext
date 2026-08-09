using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using PlaceContext.Application;
using PlaceContext.ServiceDefaults;
using PlaceContext.Infrastructure;
using PlaceContext.Jobs;
using PlaceContext.Jobs.Controllers;
using PlaceContext.Jobs.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddJobsApi();
builder.Services.AddJobsModule();
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddJobsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(JobsController).Assembly);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-ingestion", context =>
    {
        var credential = context.Request.Headers["X-Ingest-Key"].ToString();
        var partition = string.IsNullOrEmpty(credential)
            ? $"{context.Request.Host.Host}:{context.Connection.RemoteIpAddress}"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});

var app = builder.Build();
await app.Services.MigrateJobsDatabaseAsync();
if (app.Configuration.GetValue("PlaceContext:EncryptionAtRest:BootstrapOnStartup", false))
    await PlaceContext.Jobs.Infrastructure.Security.JobsEncryptionAtRestBootstrap.RunAsync(app.Services);
app.UseRateLimiter();
app.UsePlaceContextServiceRuntime("jobs");
app.Run();

public partial class Program;
