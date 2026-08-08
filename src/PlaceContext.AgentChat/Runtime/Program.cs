using PlaceContext.AgentChat;
using PlaceContext.AgentChat.Controllers;
using PlaceContext.Application;
using PlaceContext.Application.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddAgentChatApi();
builder.Services.AddAgentChatInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(AgentChatController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("agent-chat");
app.Run();

public partial class Program;
