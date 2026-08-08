using PlaceContext.Application;
using PlaceContext.Application.Runtime;
using PlaceContext.Crm;
using PlaceContext.Crm.Controllers;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddCrmApi();
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddCrmInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(CrmController).Assembly);

var app = builder.Build();
await app.Services.MigrateCrmDatabaseAsync();
await PlaceContext.Crm.Infrastructure.Security.CrmEncryptionAtRestBootstrap.RunAsync(app.Services);
app.UsePlaceContextServiceRuntime("crm");
app.Run();

public partial class Program;
