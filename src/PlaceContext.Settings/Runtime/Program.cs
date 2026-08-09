using PlaceContext.ServiceDefaults;
using PlaceContext.Settings;
using PlaceContext.Settings.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSettingsModule();
builder.Services.AddSettingsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(
    builder.Configuration,
    typeof(SettingsController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("settings");
app.Run();

public partial class Program;
