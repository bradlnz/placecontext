using PlaceContext.Application;
using PlaceContext.Application.Runtime;
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

var app = builder.Build();
await app.Services.MigrateJobsDatabaseAsync();
if (app.Configuration.GetValue("PlaceContext:EncryptionAtRest:BootstrapOnStartup", false))
    await PlaceContext.Jobs.Infrastructure.Security.JobsEncryptionAtRestBootstrap.RunAsync(app.Services);
app.UsePlaceContextServiceRuntime("jobs");
app.Run();

public partial class Program;
