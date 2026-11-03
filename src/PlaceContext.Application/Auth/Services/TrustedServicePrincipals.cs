namespace PlaceContext.Application.Auth;

/// <summary>Reserved identity only granted after management API key authentication.
/// It must never be derived from job or workflow payload data.</summary>
public static class TrustedServicePrincipals
{
    public static readonly Guid ManagementApiUserId =
        Guid.Parse("00000000-0000-0000-0000-00000000a9a9");
}
