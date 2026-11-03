using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Auth;

/// <summary>A verification code dispatched to the user.</summary>
public sealed record TwoFactorChallenge(
    string Channel,
    string MaskedDestination,
    DateTimeOffset ExpiresAt);
