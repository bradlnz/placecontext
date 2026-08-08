using PlaceContext.Application;
using PlaceContext.Application.Runtime;
using PlaceContext.Crm;
using PlaceContext.Crm.Controllers;
using PlaceContext.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddCrmApi();
builder.Services.AddInfrastructureCore(builder.Configuration);
builder.Services.AddCrmInfrastructure();
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(CrmController).Assembly);

var app = builder.Build();
app.UsePlaceContextServiceRuntime("crm");
app.Run();

public partial class Program;
