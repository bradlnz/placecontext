namespace PlaceContext.Host;

/// <summary>View data for the login verification-code page (email or SMS delivery).</summary>
public sealed record EmailVerifyModel(
    string Heading,
    string DestinationHint,
    bool RequiresPhone,
    string? SwitchHref,
    string? SwitchLabel);
