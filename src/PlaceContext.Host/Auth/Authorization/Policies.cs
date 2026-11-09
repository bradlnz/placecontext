using PlaceContext.Application.Access.Models;
namespace PlaceContext.Host.Auth;

/// <summary>Authorization policy names that are not themselves permission strings (for those the policy
/// name IS the permission — see <see cref="Permission"/>).</summary>
public static class Policies
{
    /// <summary>Persisted workspace owners and super admins can inspect application tables.</summary>
    public const string DatabaseInspection = "DatabaseInspection";

    /// <summary>Only the tenant's bootstrap default admin — gates the /settings/* area beyond the
    /// self-service API tokens page, plus the controllers backing those pages.</summary>
    public const string DefaultAdmin = "DefaultAdmin";
}
