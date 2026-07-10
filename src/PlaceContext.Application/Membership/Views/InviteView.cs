namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a freshly-created invite (the host turns the token into a join link).</summary>
public sealed record InviteView(string Email, string Role, string Token);

/// <summary>Read model: the email + role an invite token grants (shown on the join page).</summary>
public sealed record InviteInfo(string Email, string Role);
