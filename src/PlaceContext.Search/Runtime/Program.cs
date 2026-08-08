using PlaceContext.Application;
using PlaceContext.Application.Runtime;
using PlaceContext.Infrastructure;
using PlaceContext.Search;
using PlaceContext.Search.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddSearchApi();
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddSearchInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(SearchController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("search");
app.Run();

public partial class Program;
