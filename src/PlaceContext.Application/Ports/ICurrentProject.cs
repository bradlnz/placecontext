namespace PlaceContext.Application.Ports;

/// <summary>
/// The project the current request is scoped to, resolved by middleware from
/// <c>X-Project-Id</c> / <c>X-Project</c> (or the matching query string). Entity data API routes
/// (<c>/api/v1/{entity-name}</c>) read this instead of taking a project id in the path.
/// </summary>
public interface ICurrentProject
{
    Guid? ProjectId { get; }
    string? ProjectName { get; }
    bool IsResolved { get; }
}
