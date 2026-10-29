namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a freshly-created invite (the host turns the token into a join link).</summary>
public sealed record InviteView(string Email, string Role, string Token);
