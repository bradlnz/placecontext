namespace PlaceContext.Host.Auth;

internal sealed record ServiceAppGrant(ServiceAppSession Session, string State, string ReturnPath);
