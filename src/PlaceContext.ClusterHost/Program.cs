using PlaceContext.ClusterHost;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.Configure<ClusterProxyOptions>(builder.Configuration.GetSection("PlaceContext:ClusterChat"));

builder.Services.AddSingleton<ClusterProxyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClusterProxyService>());
builder.Services.AddScoped<ClusterPipeline>();

var app = builder.Build();

var shardEndpoints = builder.Configuration.GetSection("PlaceContext:ClusterChat:ShardEndpoints").Get<List<string>>() ?? new();
var model = builder.Configuration["PlaceContext:ClusterChat:Model"] ?? "qwen3.5-4b";
Console.WriteLine($"[ClusterHost] Starting on port {builder.Configuration["ASPNETCORE_HTTP_PORTS"] ?? "8081"}");
Console.WriteLine($"[ClusterHost] Model: {model}");
Console.WriteLine($"[ClusterHost] Shard endpoints: [{string.Join(", ", shardEndpoints)}]");

app.UseMiddleware<ClusterApiAuthenticationMiddleware>();
app.MapControllers();
app.Run();
