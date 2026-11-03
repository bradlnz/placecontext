namespace PlaceContext.Application.Tenancy;

/// <summary>The id + role name resolved off a request's authenticated principal.</summary>
public sealed record UserIdentity(Guid Id, string Role);
