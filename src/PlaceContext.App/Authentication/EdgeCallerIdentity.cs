using System.Text;
using System.Text.Json;

namespace PlaceContext.App.Authentication;

public sealed record EdgeCallerIdentity(string DisplayName, string Role, string Tenant)
{
    internal static EdgeCallerIdentity? FromServiceToken(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2) return null;

        try
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object) return null;
            var name = ReadClaim(payload.RootElement, "name", "unique_name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name") ?? "PlaceContext user";
            var role = ReadClaim(payload.RootElement, "role", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role") ?? "Viewer";
            var tenant = ReadClaim(payload.RootElement, "tenant_slug", "tenant");
            return tenant is null ? null : new EdgeCallerIdentity(name, role, tenant);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? ReadClaim(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (!payload.TryGetProperty(name, out var claim)) continue;
            if (claim.ValueKind == JsonValueKind.String) return claim.GetString();
            if (claim.ValueKind == JsonValueKind.Array)
            {
                var first = claim.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String) return first.GetString();
            }
        }
        return null;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return Convert.FromBase64String(padded);
    }
}
