using PlaceContext.ServiceDefaults;
using PlaceContext.Communications;
using PlaceContext.Communications.Controllers;
using PlaceContext.Communications.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCommunicationsModule();
builder.Services.AddCommunicationsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(
    builder.Configuration,
    typeof(CommunicationsController).Assembly);

var app = builder.Build();
await app.Services.MigrateCommunicationsDatabaseAsync();
app.UsePlaceContextServiceRuntime("communications");
app.Run();

public partial class Program;
