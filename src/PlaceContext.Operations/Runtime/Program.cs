using PlaceContext.ServiceDefaults;
using PlaceContext.Operations;
using PlaceContext.Operations.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOperationsModule();
builder.Services.AddOperationsInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(OperationsController).Assembly);
var app = builder.Build();
app.UsePlaceContextServiceRuntime("operations");
app.Run();
public partial class Program;
