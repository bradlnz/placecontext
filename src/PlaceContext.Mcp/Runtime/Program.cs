using PlaceContext.Application;
using PlaceContext.ServiceDefaults;
using PlaceContext.Mcp;
using PlaceContext.Mcp.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddMcpModule();
builder.Services.AddMcpInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(
    builder.Configuration,
    typeof(McpController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("mcp");
app.Run();

public partial class Program;
