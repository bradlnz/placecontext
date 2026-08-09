using PlaceContext.Application;
using PlaceContext.ServiceDefaults;
using PlaceContext.Vault;
using PlaceContext.Vault.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationCore();
builder.Services.AddVaultApi();
builder.Services.AddVaultInfrastructure(builder.Configuration);
builder.Services.AddPlaceContextServiceRuntime(builder.Configuration, typeof(VaultController).Assembly);

var app = builder.Build();
if (app.Configuration.GetValue("PlaceContext:Vault:EncryptionAtRest:BootstrapOnStartup", false))
    await PlaceContext.Vault.Infrastructure.Security.VaultEncryptionAtRestBootstrap.RunAsync(app.Services);
app.UsePlaceContextServiceRuntime("vault");
app.Run();

public partial class Program;
