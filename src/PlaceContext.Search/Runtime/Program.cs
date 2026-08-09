using PlaceContext.BuildingBlocks;
using PlaceContext.ServiceDefaults;
using PlaceContext.Search;
using PlaceContext.Search.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlaceContextCqrs();
builder.Services.AddSearchApi();
builder.Services.AddSearchInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(SearchController).Assembly);

var app = builder.Build();
await PlaceContext.Search.Infrastructure.Persistence.SearchDatabaseMigrationExtensions
    .MigrateSearchDatabaseAsync(app.Services);
app.UsePlaceContextServiceRuntime("search");
app.Run();

public partial class Program;
