using PlaceContext.Application;
using PlaceContext.Application.Runtime;
using PlaceContext.Infrastructure;
using PlaceContext.Jobs;
using PlaceContext.Jobs.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddJobsApi();
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddJobsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(JobsController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("jobs");
app.Run();

public partial class Program;
