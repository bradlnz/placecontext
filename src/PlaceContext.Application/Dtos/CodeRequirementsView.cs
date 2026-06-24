namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a single code-requirements document (global, or for one project).</summary>
public sealed record CodeRequirementsView(Guid? ProjectId, bool IsGlobal, string Markdown, bool IsEmpty, DateTimeOffset? UpdatedAt)
{
    public static CodeRequirementsView EmptyGlobal() => new(null, true, string.Empty, true, null);
    public static CodeRequirementsView EmptyForProject(Guid projectId) => new(projectId, false, string.Empty, true, null);
}
