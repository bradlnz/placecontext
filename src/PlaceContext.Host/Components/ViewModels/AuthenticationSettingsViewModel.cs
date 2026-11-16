using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AuthenticationSettingsViewModel(IConfiguration configuration, NavigationManager navigation) : PageViewModel
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
    public string Protocol { get; set; } = "OpenIdConnect";
    public string OAuthClientId { get; set; } = "";
    public string OAuthName { get; set; } = "";
    public string OAuthRedirect { get; set; } = "";
    public string? Generated { get; private set; }
    public string? Error { get; private set; }
    public bool SsoConfigured { get; private set; }
    public int TrustedClientCount { get; private set; }
    public string Origin => navigation.BaseUri.TrimEnd('/');

    public void Initialize()
    {
        var sso = configuration.GetSection("PlaceContext:Sso");
        Authority = sso["Authority"] ?? "";
        ClientId = sso["ClientId"] ?? "";
        CallbackUrl = sso["CallbackUrl"] ?? Origin + "/auth/sso/callback";
        Protocol = sso["Protocol"] ?? "OAuth";
        SsoConfigured = Https(Authority) && Https(CallbackUrl) && !string.IsNullOrWhiteSpace(ClientId);
        TrustedClientCount = configuration.GetSection("PlaceContext:OAuth:TrustedClients").GetChildren().Count();
    }

    public void Generate(bool sso)
    {
        Error = null;
        Generated = null;
        if (sso && (!Https(Authority) || !Https(CallbackUrl) || string.IsNullOrWhiteSpace(ClientId) || ClientId.Length > 256)
            || !sso && (!Https(OAuthRedirect) || string.IsNullOrWhiteSpace(OAuthClientId) || OAuthClientId.Length > 256))
        {
            Error = "Enter a client ID and HTTPS URLs without credentials or fragments.";
            return;
        }
        object settings = sso
            ? new { PlaceContext = new { Sso = new { Authority = Authority.Trim(), ClientId = ClientId.Trim(), CallbackUrl = CallbackUrl.Trim(), Protocol } } }
            : new { PlaceContext = new { OAuth = new { TrustedClients = new[] { new { ClientId = OAuthClientId.Trim(), Name = OAuthName.Trim(), RedirectUri = OAuthRedirect.Trim() } } } } };
        Generated = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
