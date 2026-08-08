namespace PlaceContext.Host.Controllers;

public sealed record SetupCliRequest(
    string Email,
    string? DisplayName,
    string Password,
    string? ConfirmPassword);
