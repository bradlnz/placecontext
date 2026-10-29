using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>Translates between the management API's project DTOs and the internal read model.</summary>
internal static class ProjectApiMapper
{
    public static ProjectResponse ToResponse(ProjectSummaryView v) => new(
        v.Id, v.Name, v.Path, v.Status, v.IsGraphified);
}
