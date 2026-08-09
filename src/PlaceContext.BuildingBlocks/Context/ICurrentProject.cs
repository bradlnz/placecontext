namespace PlaceContext.Application.Ports;

/// <summary>
/// The project the current request is scoped to, resolved by the hosting request pipeline from
/// <c>X-Project-Id</c> / <c>X-Project</c> (or the matching query string).
/// </summary>
public interface ICurrentProject
{
    Guid ProjectId { get; }
    string ProjectName { get; }
    bool IsResolved { get; }
}
