namespace PlaceContext.Identity.Contracts.Api;

public sealed record SetPermissionOverrideRequest(string Permission, bool? Allowed);
