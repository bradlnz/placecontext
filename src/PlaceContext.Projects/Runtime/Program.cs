using PlaceContext.Application;
using PlaceContext.ServiceDefaults;
using PlaceContext.Projects;
using PlaceContext.Projects.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddProjectsModule();
builder.Services.AddProjectsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(
    builder.Configuration,
    typeof(ProjectsController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("projects");
app.Run();

public partial class Program;
