using PlaceContext.Agents;
using PlaceContext.Agents.Controllers;
using PlaceContext.Agents.Infrastructure.Persistence;
using PlaceContext.Application;
using PlaceContext.Application.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddAgentsApi();
builder.Services.AddAgentsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(AgentsController).Assembly);

var app = builder.Build();
await app.Services.MigrateAgentsDatabaseAsync();
app.UsePlaceContextServiceRuntime("agents");
app.Run();

public partial class Program;
