namespace PlaceContext.Identity.Contracts.Api;

public sealed record SetupCliRequest(
    string Email,
    string? DisplayName,
    string Password,
    string? ConfirmPassword);
