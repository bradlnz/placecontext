using PlaceContext.Application.Dtos;

namespace PlaceContext.Projects.Api;

internal static class ProjectApiMapper
{
    public static ProjectResponse ToResponse(ProjectSummaryView project) => new(
        project.Id,
        project.Name,
        project.Path,
        project.Status,
        project.IsGraphified);
}
