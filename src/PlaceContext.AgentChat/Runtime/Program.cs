using PlaceContext.AgentChat;
using PlaceContext.AgentChat.Controllers;
using PlaceContext.Application;
using PlaceContext.ServiceDefaults;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddAgentChatModule();
builder.Services.AddAgentChatInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(AgentChatController).Assembly);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-ingestion", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Request.Host.Host}:{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();
app.UseRateLimiter();
app.UsePlaceContextServiceRuntime("agent-chat");
app.Run();

public partial class Program;
