using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>OAuth client registration. Global (clients are not tenant-scoped); redirect URIs as JSON.</summary>
public sealed class OAuthClientRow
{
    public string ClientId { get; set; } = "";
    public string RedirectUris { get; set; } = "[]";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
