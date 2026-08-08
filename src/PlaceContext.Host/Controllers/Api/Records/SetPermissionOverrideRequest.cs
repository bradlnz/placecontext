namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SetPermissionOverrideRequest(string Permission, bool? Allowed);
