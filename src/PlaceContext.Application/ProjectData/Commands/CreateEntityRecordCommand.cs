using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

/// <summary>Insert one entity/table row. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record CreateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Values) : ICommand<CreateEntityRecordResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
