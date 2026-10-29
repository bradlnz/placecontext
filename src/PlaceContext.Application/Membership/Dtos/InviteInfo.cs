namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the email + role an invite token grants (shown on the join page).</summary>
public sealed record InviteInfo(string Email, string Role);
