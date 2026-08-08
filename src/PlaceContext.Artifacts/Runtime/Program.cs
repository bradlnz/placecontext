using PlaceContext.Application;
using PlaceContext.Application.Runtime;
using PlaceContext.Artifacts;
using PlaceContext.Artifacts.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddArtifactsApi();
builder.Services.AddArtifactsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(ArtifactsController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("artifacts");
app.Run();

public partial class Program;
