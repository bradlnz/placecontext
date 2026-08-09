namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a single requirements document (global, or for one project).</summary>
public sealed record RequirementsView(Guid? ProjectId, bool IsGlobal, string Markdown, bool IsEmpty, DateTimeOffset? UpdatedAt)
{
    public static RequirementsView EmptyGlobal() => new(null, true, string.Empty, true, null);
    public static RequirementsView EmptyForProject(Guid projectId) => new(projectId, false, string.Empty, true, null);
}
