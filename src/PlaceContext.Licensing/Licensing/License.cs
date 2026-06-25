namespace PlaceContext.Licensing.Licensing;

/// <summary>A licensed deployment record: the activation <see cref="Key"/> a customer pastes into their
/// deployment, and the organisation / plan claims minted into their entitlement token.</summary>
public sealed record License(string Key, string Org, string Plan);
