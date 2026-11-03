using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

/// <summary>Delete a row matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record DeleteEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
