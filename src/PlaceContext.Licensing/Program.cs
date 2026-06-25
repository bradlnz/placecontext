using PlaceContext.Licensing.Licensing;
using PlaceContext.Licensing.Models;
using PlaceContext.Licensing.Payments;
using PlaceContext.Licensing.Signing;

var builder = WebApplication.CreateBuilder(args);

// ---- Signing key (the licensor's root of trust) ----------------------------------------------------
// Prefer a configured PEM so the server's root public key matches what deployments pin as their anchor
// (PlaceContext:Activation:PublicKey). With none configured a fresh key is generated and its public key
// is logged at startup so an operator can copy it into the deployments.
var rootPem = builder.Configuration["Licensing:SigningKeyPem"]
    ?? Environment.GetEnvironmentVariable("LICENSING_SIGNING_KEY_PEM");
if (string.IsNullOrWhiteSpace(rootPem))
{
    var pemPath = builder.Configuration["Licensing:SigningKeyPath"];
    if (!string.IsNullOrWhiteSpace(pemPath) && File.Exists(pemPath))
        rootPem = File.ReadAllText(pemPath);
}

var signer = new ActivationSigner(rootPem);

// ---- License catalogue + payment provider ----------------------------------------------------------
var licenses = builder.Configuration.GetSection("Licensing:Licenses").Get<List<LicenseConfig>>();
if (licenses is null || licenses.Count == 0)
    licenses = new List<LicenseConfig> { new() { Key = "DEMO-PLACECONTEXT", Org = "Demo Org", Plan = "pro" } };
var licenseStore = new LicenseStore(licenses.Select(l => new License(l.Key, l.Org, l.Plan)));

var overrides = new Dictionary<string, SubscriptionStatus>(StringComparer.OrdinalIgnoreCase);
foreach (var child in builder.Configuration.GetSection("Licensing:Subscriptions").GetChildren())
    if (Enum.TryParse<SubscriptionStatus>(child.Value, ignoreCase: true, out var status))
        overrides[child.Key] = status;
IPaymentProvider payments = new StubPaymentProvider(overrides);

var tokenLifetime = TimeSpan.FromDays(
    double.TryParse(builder.Configuration["Licensing:TokenLifetimeDays"], out var d) && d > 0 ? d : 30d);
var adminKey = builder.Configuration["Licensing:AdminKey"];

builder.Services.AddSingleton(signer);

var app = builder.Build();

app.Logger.LogInformation(
    "PlaceContext Licensing server starting. Active signing kid {Kid}. Anchor public key (pin as " +
    "PlaceContext:Activation:PublicKey on deployments):\n{PublicKey}",
    signer.ActiveKid, signer.RootPublicKeyBase64);

// ---- Endpoints -------------------------------------------------------------------------------------

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// The current signing-key rotation set (root + endorsed successors) — clients poll this to follow rotation.
app.MapGet("/keys", () => Results.Ok(new KeysResponse(signer.KeySet())));

// Activate a deployment: validate the license key, gate on its subscription, and mint a signed entitlement
// token (plus the rotation key set so the client can follow key changes).
app.MapPost("/activate", (ActivateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Key))
        return Results.Json(new ErrorResponse("An activation key is required."), statusCode: StatusCodes.Status400BadRequest);

    var license = licenseStore.Find(request.Key);
    if (license is null)
        return Results.Json(new ErrorResponse("Unknown activation key."), statusCode: StatusCodes.Status403Forbidden);

    switch (payments.StatusFor(license.Key))
    {
        case SubscriptionStatus.PastDue:
            return Results.Json(
                new ErrorResponse("Subscription payment is past due — settle the invoice to reactivate."),
                statusCode: StatusCodes.Status402PaymentRequired);
        case SubscriptionStatus.Canceled:
            return Results.Json(
                new ErrorResponse("Subscription has been cancelled."),
                statusCode: StatusCodes.Status403Forbidden);
    }

    var token = signer.SignToken(license.Org, license.Plan, DateTimeOffset.UtcNow.Add(tokenLifetime));
    app.Logger.LogInformation("Activated {Org} ({Plan}) for instance {Instance}.",
        license.Org, license.Plan, request.InstanceId ?? "(unspecified)");
    return Results.Ok(new ActivateResponse(token, signer.KeySet()));
});

// Rotate the active signing key (demonstrates key rotation). Guarded by an admin key; disabled when unset.
app.MapPost("/admin/rotate", (HttpRequest http) =>
{
    if (string.IsNullOrWhiteSpace(adminKey))
        return Results.NotFound();
    if (http.Headers["X-Admin-Key"].ToString() != adminKey)
        return Results.Json(new ErrorResponse("Invalid admin key."), statusCode: StatusCodes.Status403Forbidden);

    var kid = signer.Rotate();
    app.Logger.LogWarning("Signing key rotated. New active kid {Kid}.", kid);
    return Results.Ok(new { rotatedTo = kid, keys = signer.KeySet() });
});

app.Run();

/// <summary>Configuration shape for a seeded license row (<c>Licensing:Licenses</c>).</summary>
internal sealed class LicenseConfig
{
    public string Key { get; set; } = "";
    public string Org { get; set; } = "";
    public string Plan { get; set; } = "";
}

/// <summary>Exposed so the licensing server can be hosted in an integration test via WebApplicationFactory.</summary>
public partial class Program { }
