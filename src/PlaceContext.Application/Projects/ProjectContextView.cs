namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a project's single Markdown context document.</summary>
public sealed record ProjectContextView(Guid ProjectId, string Markdown, bool IsEmpty, DateTimeOffset? UpdatedAt)
{
    public static ProjectContextView Empty(Guid projectId) => new(projectId, string.Empty, true, null);
}
