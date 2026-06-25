using System.Text.Json.Serialization;

namespace PlaceContext.Infrastructure.Activation;

/// <summary>What the deployment sends the licensing server to activate: its license key plus a stable
/// instance id (so the server can attribute usage / seats to this deployment).</summary>
internal sealed record ActivateRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("instanceId")] string InstanceId);

/// <summary>The licensing server's activation response: a freshly-signed entitlement <see cref="Token"/>
/// (same <c>payload.signature</c> format the offline path verifies) and the current set of signing
/// <see cref="Keys"/> so the client can follow key rotation.</summary>
internal sealed record ActivateResponse(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("keys")] IReadOnlyList<PublicKeyEntry>? Keys);

/// <summary>The server's reply to <c>GET /keys</c>: the current rotation key set.</summary>
internal sealed record KeysResponse(
    [property: JsonPropertyName("keys")] IReadOnlyList<PublicKeyEntry>? Keys);

/// <summary>
/// One signing public key in the licensor's rotation set. <see cref="Spki"/> is the base64 SubjectPublicKeyInfo.
/// The root key has no endorsement (the client matches it against its configured anchor). A rotated-in key
/// carries <see cref="EndorsedByKid"/> + <see cref="Endorsement"/> — an ECDSA signature, by the endorsing key,
/// over this key's SPKI bytes — so the client can extend trust forward from the anchor without a config change.
/// </summary>
internal sealed record PublicKeyEntry(
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("spki")] string Spki,
    [property: JsonPropertyName("endorsedBy")] string? EndorsedByKid,
    [property: JsonPropertyName("endorsement")] string? Endorsement);
