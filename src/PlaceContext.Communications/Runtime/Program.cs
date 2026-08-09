using PlaceContext.ServiceDefaults;
using PlaceContext.Communications;
using PlaceContext.Communications.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCommunicationsModule();
builder.Services.AddCommunicationsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(
    builder.Configuration,
    typeof(CommunicationsController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("communications");
app.Run();

public partial class Program;
