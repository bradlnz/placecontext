using PlaceContext.Agents;
using PlaceContext.Agents.Controllers;
using PlaceContext.Agents.Infrastructure.Persistence;
using PlaceContext.BuildingBlocks;
using PlaceContext.ServiceDefaults;
using PlaceContext.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlaceContextCqrs();
builder.Services.AddAgentsApi();
builder.Services.AddAgentsInfrastructure(builder.Configuration);
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(AgentsController).Assembly);

var app = builder.Build();
await app.Services.MigrateAgentsDatabaseAsync();
app.UsePlaceContextServiceRuntime("agents");
app.Run();

public partial class Program;
