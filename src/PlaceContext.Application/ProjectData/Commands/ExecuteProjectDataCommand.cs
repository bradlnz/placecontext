using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;
namespace PlaceContext.Application.Features;

/// <summary>Run SQL inside a project's own database (a command — SQL may write).</summary>
public sealed record ExecuteProjectDataCommand(Guid ProjectId, string Sql) : ICommand<ProjectQueryResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
