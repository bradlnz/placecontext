using System.Text.Json.Serialization;

namespace PlaceContext.Licensing.Models;

/// <summary>A deployment's activation request: its license key and a stable instance id.</summary>
public sealed record ActivateRequest(
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("instanceId")] string? InstanceId);

/// <summary>The activation response: a freshly-signed entitlement token plus the current rotation key set.</summary>
public sealed record ActivateResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("keys")] IReadOnlyList<PublicKeyEntry> Keys);

/// <summary>The current rotation key set (also returned from <c>GET /keys</c>).</summary>
public sealed record KeysResponse(
    [property: JsonPropertyName("keys")] IReadOnlyList<PublicKeyEntry> Keys);

/// <summary>
/// One signing public key in the rotation set. The root key carries no endorsement (clients anchor to it);
/// a rotated-in key carries <see cref="EndorsedBy"/> + <see cref="Endorsement"/> — a signature by the
/// endorsing key over this key's SPKI bytes — so a client can extend trust forward without a config change.
/// <see cref="Spki"/> and <see cref="Endorsement"/> are standard base64; the client decodes them directly.
/// </summary>
public sealed record PublicKeyEntry(
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("spki")] string Spki,
    [property: JsonPropertyName("endorsedBy")] string? EndorsedBy,
    [property: JsonPropertyName("endorsement")] string? Endorsement);

/// <summary>A simple error envelope returned with 4xx denials.</summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error);
