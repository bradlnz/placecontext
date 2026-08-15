using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>The record-create outcome: the row is always kept — existing identity values only warn.</summary>
public sealed record CreateEntityRecordResult(IReadOnlyList<string> DuplicateWarnings);

/// <summary>Insert one entity/table row. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record CreateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Values) : ICommand<CreateEntityRecordResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

/// <summary>Update a row matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record UpdateEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys,
    IReadOnlyDictionary<string, string?> Values) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

/// <summary>Delete a row matching key columns. Requires <see cref="Permission.DataWrite"/>.</summary>
public sealed record DeleteEntityRecordCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyDictionary<string, string?> Keys) : ICommand<int>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
