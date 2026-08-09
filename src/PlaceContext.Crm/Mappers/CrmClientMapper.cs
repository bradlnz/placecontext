using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class CrmClientMapper
{
    public static CrmClientView ToView(CrmClient client) => new(
        client.Id,
        client.ProjectId,
        client.Name,
        client.Company,
        client.Email,
        client.Phone,
        client.LifecycleStage.ToString(),
        client.Notes,
        client.CustomerPortalEnabled,
        client.CustomerPortalSlug,
        client.CustomerPortalDomain,
        client.CustomerPortalBrandName,
        client.CustomerPortalLogoUrl,
        client.CreatedAt,
        client.UpdatedAt);
}
