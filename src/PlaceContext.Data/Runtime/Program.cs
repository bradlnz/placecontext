using PlaceContext.BuildingBlocks;
using PlaceContext.ServiceDefaults;
using PlaceContext.Data;
using PlaceContext.Data.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlaceContextCqrs();
builder.Services.AddDataApi();
builder.Services.AddDataInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(DataController).Assembly);

var app = builder.Build();
await PlaceContext.Data.Infrastructure.Persistence.DataDatabaseMigrationExtensions
    .MigrateDataDatabaseAsync(app.Services);
app.UsePlaceContextServiceRuntime("data");
app.Run();

public partial class Program;
