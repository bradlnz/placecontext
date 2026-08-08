using PlaceContext.Application;
using PlaceContext.Application.Runtime;
using PlaceContext.Data;
using PlaceContext.Data.Controllers;
using PlaceContext.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddDataApi();
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddDataInfrastructure();
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(DataController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("data");
app.Run();

public partial class Program;
