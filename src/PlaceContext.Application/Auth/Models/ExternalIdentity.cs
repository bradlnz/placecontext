namespace PlaceContext.Application.Auth;

public sealed record ExternalIdentity(string Subject, string Email, string? Name, string? Role);
